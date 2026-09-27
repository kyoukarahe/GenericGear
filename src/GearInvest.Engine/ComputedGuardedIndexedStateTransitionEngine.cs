using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class ComputedGuardedIndexedStateTransitionEngine
{
    public ComputedGuardedHybridStateTransitionResult Apply(
        HybridStateSnapshot initialSnapshot,
        ComputedGuardedIndexedStateTransitionPlan transitionPlan,
        ContinuousEventBridgeResult eventResult,
        GuardedHybridStateAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        if (transitionPlan is null) throw new ArgumentNullException(nameof(transitionPlan));
        if (eventResult is null) throw new ArgumentNullException(nameof(eventResult));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var totalWatch = Stopwatch.StartNew();
        var validationWatch = Stopwatch.StartNew();
        var diagnostics = new List<Diagnostic>();
        var invalid = false;
        var unsupported = false;
        var requestId = ComputedGuardedHybridStateTransitionIdentity.ComputeAdvanceRequestId(
            initialSnapshot,
            transitionPlan,
            eventResult.EventBridgeRequestId,
            request);

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                ComputedGuardedHybridStateTransitionContract.DefaultDeterminismProfile) ||
            !StringComparer.Ordinal.Equals(
                transitionPlan.DeterminismProfile,
                ComputedGuardedHybridStateTransitionContract.DefaultDeterminismProfile))
        {
            unsupported = true;
            diagnostics.Add(Error(
                DiagnosticCodes.StateGuardedUnsupportedProfile,
                "The computed guarded indexed-state determinism profile is unsupported."));
        }

        if (request.MaxAppliedOccurrences <= 0)
        {
            invalid = true;
            diagnostics.Add(Error(
                DiagnosticCodes.StateGuardedInvalidBudget,
                "maxAppliedOccurrences must be greater than zero."));
        }

        var model = ValidatePlan(transitionPlan, diagnostics, ref invalid, ref unsupported);
        ValidateSnapshot(initialSnapshot, model, diagnostics, ref invalid);
        ValidateEventEnvelope(initialSnapshot, eventResult, model.EventDefinitionIds, diagnostics, ref invalid);
        if (!invalid && !unsupported)
        {
            ValidateConstraints(ToStateDictionary(initialSnapshot), model, diagnostics, ref invalid);
        }

        validationWatch.Stop();
        if (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateGuardedCancelled,
                "Computed guarded indexed-state transition was cancelled before occurrence application."));
            totalWatch.Stop();
            return Terminal(
                eventResult,
                transitionPlan,
                request,
                requestId,
                GuardedHybridStateTransitionStatus.Cancelled,
                initialSnapshot,
                diagnostics,
                new GuardedHybridStateTransitionPerformance(
                    ToMicroseconds(validationWatch.ElapsedTicks), 0, 0, 0, 0, 0,
                    ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        var occurrenceWatch = Stopwatch.StartNew();
        var occurrences = eventResult.Occurrences
            .OrderBy(item => item.RootTurnsAtCrossing)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.OccurrenceId, StringComparer.Ordinal)
            .ToList();
        ValidateOccurrences(initialSnapshot, eventResult, model.EventDefinitionIds, occurrences, diagnostics, ref invalid);
        occurrenceWatch.Stop();

        if (invalid || unsupported)
        {
            totalWatch.Stop();
            return Terminal(
                eventResult,
                transitionPlan,
                request,
                requestId,
                invalid ? GuardedHybridStateTransitionStatus.InvalidInput : GuardedHybridStateTransitionStatus.Unsupported,
                initialSnapshot,
                diagnostics,
                new GuardedHybridStateTransitionPerformance(
                    ToMicroseconds(validationWatch.ElapsedTicks),
                    ToMicroseconds(occurrenceWatch.ElapsedTicks),
                    0, 0, 0, 0,
                    ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        var stateValues = ToStateDictionary(initialSnapshot);
        var cursors = initialSnapshot.EventCursors.ToDictionary(
            item => item.PeriodicEventDefinitionId,
            item => item.LastAppliedOrdinal,
            StringComparer.Ordinal);
        var unconditionalApplications = new List<StateTransitionApplication>();
        var guardedApplications = new List<GuardedStateEffectApplication>();
        var computedEvaluations = new List<ComputedIndexedValueEvaluation>();
        var groups = new List<GuardedOccurrenceApplicationGroup>();
        var appliedOccurrenceCount = 0;
        var guardTicks = 0L;
        var effectTicks = 0L;
        var commitTicks = 0L;
        var checkpointRoot = initialSnapshot.ExactRootTurns;
        var cancelled = false;
        var maximum = Math.Min(request.MaxAppliedOccurrences, occurrences.Count);

        for (var occurrenceIndex = 0; occurrenceIndex < maximum; occurrenceIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateGuardedCancelled,
                    "Computed guarded indexed-state transition observed cancellation between occurrence groups."));
                break;
            }

            var occurrence = occurrences[occurrenceIndex];
            if (!ValidateConstraints(stateValues, model, diagnostics, ref invalid)) break;

            var guardWatch = Stopwatch.StartNew();
            var computedSelections = EvaluateComputedDefinitions(stateValues, model, diagnostics, ref invalid);
            if (invalid)
            {
                guardWatch.Stop();
                guardTicks += guardWatch.ElapsedTicks;
                break;
            }

            var choice = model.ChoicesByEvent[occurrence.PeriodicEventDefinitionId];
            var matchingBranches = choice.OrderedBranches
                .Where(branch => EvaluateGuard(
                    branch.Guard,
                    stateValues,
                    computedSelections,
                    model,
                    diagnostics,
                    ref invalid))
                .ToList();
            guardWatch.Stop();
            guardTicks += guardWatch.ElapsedTicks;
            if (invalid) break;

            if (matchingBranches.Count == 0)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateGuardNoMatch,
                    "ExactlyOne computed guarded choice had no matching branch.",
                    choice.GuardedTransitionChoiceId));
                break;
            }

            if (matchingBranches.Count != 1)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateGuardAmbiguous,
                    "ExactlyOne computed guarded choice had more than one matching branch.",
                    choice.GuardedTransitionChoiceId));
                break;
            }

            var selectedBranch = matchingBranches[0];
            var effectWatch = Stopwatch.StartNew();
            var pendingValues = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
            var pendingUnconditional = new List<StateTransitionApplication>();
            var pendingGuarded = new List<GuardedStateEffectApplication>();
            var targetIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var transition in model.UnconditionalTransitionsByEvent[occurrence.PeriodicEventDefinitionId])
            {
                if (!targetIds.Add(transition.TargetCyclicStateDefinitionId))
                {
                    AddTargetConflict(transition.TargetCyclicStateDefinitionId, diagnostics, ref invalid);
                    break;
                }

                var definition = model.StateDefinitions[transition.TargetCyclicStateDefinitionId];
                var before = stateValues[transition.TargetCyclicStateDefinitionId];
                var after = EuclideanModulo(before + transition.SignedDelta, definition.CycleLength);
                pendingValues.Add(transition.TargetCyclicStateDefinitionId, after);
                pendingUnconditional.Add(new StateTransitionApplication(
                    transition.IndexedStateTransitionDefinitionId,
                    occurrence.OccurrenceId,
                    occurrence.PeriodicEventDefinitionId,
                    occurrence.EventOrdinal,
                    occurrence.RootTurnsAtCrossing,
                    transition.TargetCyclicStateDefinitionId,
                    before,
                    transition.SignedDelta,
                    after));
            }

            if (!invalid)
            {
                foreach (var effect in selectedBranch.OrderedEffects)
                {
                    if (!targetIds.Add(effect.TargetStateDefinitionId))
                    {
                        AddTargetConflict(effect.TargetStateDefinitionId, diagnostics, ref invalid);
                        break;
                    }

                    var definition = model.StateDefinitions[effect.TargetStateDefinitionId];
                    var before = stateValues[effect.TargetStateDefinitionId];
                    BigInteger after;
                    switch (effect.EffectKind)
                    {
                        case GuardedStateEffectKind.AddModulo:
                            after = EuclideanModulo(before + effect.Operand, definition.CycleLength);
                            break;
                        case GuardedStateEffectKind.SetIndex:
                            if (effect.Operand < BigInteger.Zero || effect.Operand >= definition.CycleLength)
                            {
                                invalid = true;
                                diagnostics.Add(Error(
                                    DiagnosticCodes.StateEffectInvalidIndex,
                                    "SetIndex operand is outside the target state's canonical range.",
                                    effect.GuardedStateEffectId));
                                continue;
                            }

                            after = effect.Operand;
                            break;
                        default:
                            unsupported = true;
                            diagnostics.Add(Error(
                                DiagnosticCodes.StateEffectInvalidDefinition,
                                "The computed guarded effect kind is unsupported.",
                                effect.GuardedStateEffectId));
                            continue;
                    }

                    pendingValues.Add(effect.TargetStateDefinitionId, after);
                    pendingGuarded.Add(new GuardedStateEffectApplication(
                        effect.GuardedStateEffectId,
                        occurrence.OccurrenceId,
                        occurrence.PeriodicEventDefinitionId,
                        occurrence.EventOrdinal,
                        occurrence.RootTurnsAtCrossing,
                        effect.TargetStateDefinitionId,
                        effect.EffectKind,
                        effect.Operand,
                        before,
                        after));
                }
            }

            var pendingState = new Dictionary<string, BigInteger>(stateValues, StringComparer.Ordinal);
            foreach (var update in pendingValues) pendingState[update.Key] = update.Value;
            if (!invalid && !unsupported)
            {
                ValidateConstraints(pendingState, model, diagnostics, ref invalid);
            }

            effectWatch.Stop();
            effectTicks += effectWatch.ElapsedTicks;
            if (invalid || unsupported) break;

            var applicationIds = pendingUnconditional
                .Select(item => item.TransitionApplicationId)
                .Concat(pendingGuarded.Select(item => item.GuardedStateEffectApplicationId))
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            var cursorBefore = cursors[occurrence.PeriodicEventDefinitionId];
            var group = new GuardedOccurrenceApplicationGroup(
                occurrence.OccurrenceId,
                occurrence.PeriodicEventDefinitionId,
                occurrence.EventOrdinal,
                occurrence.RootTurnsAtCrossing,
                selectedBranch.GuardedTransitionBranchId,
                applicationIds,
                cursorBefore,
                occurrence.EventOrdinal);

            var commitWatch = Stopwatch.StartNew();
            foreach (var update in pendingValues) stateValues[update.Key] = update.Value;
            unconditionalApplications.AddRange(pendingUnconditional);
            guardedApplications.AddRange(pendingGuarded);
            computedEvaluations.AddRange(computedSelections.Values.Select(selection =>
                new ComputedIndexedValueEvaluation(
                    occurrence.OccurrenceId,
                    occurrence.PeriodicEventDefinitionId,
                    occurrence.EventOrdinal,
                    occurrence.RootTurnsAtCrossing,
                    selection.Definition.ComputedIndexedValueDefinitionId,
                    selection.Case.ComputedIndexedValueCaseId,
                    selection.OutputIndex)));
            cursors[occurrence.PeriodicEventDefinitionId] = occurrence.EventOrdinal;
            groups.Add(group);
            appliedOccurrenceCount++;
            checkpointRoot = occurrence.RootTurnsAtCrossing;
            commitWatch.Stop();
            commitTicks += commitWatch.ElapsedTicks;
        }

        if (invalid || unsupported)
        {
            totalWatch.Stop();
            return Terminal(
                eventResult,
                transitionPlan,
                request,
                requestId,
                invalid ? GuardedHybridStateTransitionStatus.InvalidInput : GuardedHybridStateTransitionStatus.Unsupported,
                initialSnapshot,
                diagnostics,
                new GuardedHybridStateTransitionPerformance(
                    ToMicroseconds(validationWatch.ElapsedTicks),
                    ToMicroseconds(occurrenceWatch.ElapsedTicks),
                    ToMicroseconds(guardTicks),
                    ToMicroseconds(effectTicks),
                    ToMicroseconds(commitTicks),
                    0,
                    ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        var snapshotWatch = Stopwatch.StartNew();
        var known = occurrences.Count;
        var omitted = known - appliedOccurrenceCount;
        var status = cancelled
            ? GuardedHybridStateTransitionStatus.Cancelled
            : omitted > 0
                ? GuardedHybridStateTransitionStatus.IncompleteBudget
                : GuardedHybridStateTransitionStatus.Complete;
        var snapshotRoot = status == GuardedHybridStateTransitionStatus.Complete
            ? eventResult.NormalizedRequest.CurrentRootTurns
            : checkpointRoot;
        ValidateConstraints(stateValues, model, diagnostics, ref invalid);
        if (invalid)
        {
            snapshotWatch.Stop();
            totalWatch.Stop();
            return Terminal(
                eventResult,
                transitionPlan,
                request,
                requestId,
                GuardedHybridStateTransitionStatus.InvalidInput,
                initialSnapshot,
                diagnostics,
                new GuardedHybridStateTransitionPerformance(
                    ToMicroseconds(validationWatch.ElapsedTicks),
                    ToMicroseconds(occurrenceWatch.ElapsedTicks),
                    ToMicroseconds(guardTicks),
                    ToMicroseconds(effectTicks),
                    ToMicroseconds(commitTicks),
                    ToMicroseconds(snapshotWatch.ElapsedTicks),
                    ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        var materializedSnapshot = CreateSnapshot(initialSnapshot, snapshotRoot, stateValues, cursors);
        var checkpoint = status == GuardedHybridStateTransitionStatus.Complete ? null : materializedSnapshot;
        var finalSnapshot = status == GuardedHybridStateTransitionStatus.Complete ? materializedSnapshot : null;
        if (status == GuardedHybridStateTransitionStatus.IncompleteBudget)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.StateGuardedResultTruncated,
                DiagnosticSeverity.Warning,
                "The canonical occurrence sequence exceeded maxAppliedOccurrences."));
        }

        snapshotWatch.Stop();
        totalWatch.Stop();
        return new ComputedGuardedHybridStateTransitionResult(
            eventResult,
            transitionPlan,
            request,
            requestId,
            status,
            status == GuardedHybridStateTransitionStatus.Complete,
            status == GuardedHybridStateTransitionStatus.IncompleteBudget,
            new GuardedHybridStateTransitionSummary(
                known,
                appliedOccurrenceCount,
                omitted,
                groups.Count,
                unconditionalApplications.Count + guardedApplications.Count),
            initialSnapshot,
            unconditionalApplications,
            guardedApplications,
            computedEvaluations,
            groups,
            checkpoint,
            finalSnapshot,
            diagnostics,
            new GuardedHybridStateTransitionPerformance(
                ToMicroseconds(validationWatch.ElapsedTicks),
                ToMicroseconds(occurrenceWatch.ElapsedTicks),
                ToMicroseconds(guardTicks),
                ToMicroseconds(effectTicks),
                ToMicroseconds(commitTicks),
                ToMicroseconds(snapshotWatch.ElapsedTicks),
                ToMicroseconds(totalWatch.ElapsedTicks)));
    }

    private static ValidatedModel ValidatePlan(
        ComputedGuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid,
        ref bool unsupported)
    {
        var model = new ValidatedModel();
        var stateKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in plan.OrderedStateDefinitions)
        {
            if (string.IsNullOrWhiteSpace(state.Key) || state.CycleLength <= BigInteger.Zero)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateInvalidDefinition,
                    "A computed guarded-plan state requires a stable key and positive cycle length.",
                    state.CyclicStateDefinitionId));
            }

            if (!stateKeys.Add(state.Key) || !model.StateDefinitions.TryAdd(state.CyclicStateDefinitionId, state))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateDuplicateDefinition,
                    "Computed guarded-plan state keys and IDs must be unique.",
                    state.CyclicStateDefinitionId));
            }
        }

        if (model.StateDefinitions.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid,
                "A computed guarded plan requires at least one state."));
        }

        ValidateGuards(plan, model, diagnostics, ref invalid, ref unsupported);
        ValidateComputedDefinitions(plan, model, diagnostics, ref invalid, ref unsupported);

        var tableKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in plan.OrderedLookupTables)
        {
            var tableInvalid = false;
            if (string.IsNullOrWhiteSpace(table.Key) ||
                !StringComparer.Ordinal.Equals(
                    table.SemanticProfile,
                    ComputedGuardedIndexedStateContract.LookupSemanticProfile) ||
                table.OrderedSelectors.Count != 2 ||
                table.OrderedSelectors.Select(item => item.CanonicalReference)
                    .Distinct(StringComparer.Ordinal).Count() != 2)
            {
                tableInvalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupInvalidDefinition,
                    "A computed-selector lookup requires a stable key, supported profile and exactly two distinct selectors.",
                    table.ComputedSelectorIndexedLookupTableId));
            }

            if (!tableKeys.Add(table.Key) ||
                model.LookupTables.ContainsKey(table.ComputedSelectorIndexedLookupTableId))
            {
                tableInvalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupDuplicateDefinition,
                    "Computed-selector lookup keys and IDs must be unique.",
                    table.ComputedSelectorIndexedLookupTableId));
            }

            foreach (var selector in table.OrderedSelectors)
            {
                var extentMatches = selector.SelectorKind switch
                {
                    ComputedIndexedSelectorKind.State =>
                        model.StateDefinitions.TryGetValue(selector.ReferenceId, out var state) &&
                        state.CycleLength == selector.Extent,
                    ComputedIndexedSelectorKind.ComputedIndexedValue =>
                        model.ComputedDefinitions.TryGetValue(selector.ReferenceId, out var computed) &&
                        computed.OutputCardinality == selector.Extent,
                    _ => false,
                };
                if (string.IsNullOrWhiteSpace(selector.ReferenceId) ||
                    selector.Extent <= BigInteger.Zero ||
                    !extentMatches)
                {
                    tableInvalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupInvalidDefinition,
                        "Computed-selector kind, reference and extent must resolve exactly.",
                        table.ComputedSelectorIndexedLookupTableId));
                }
            }

            var tupleValues = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
            foreach (var entry in table.OrderedEntries)
            {
                var coordinates = entry.OrderedCoordinates;
                if (coordinates.Count != 2 ||
                    coordinates.Select(item => item.CanonicalReference).Distinct(StringComparer.Ordinal).Count() != 2 ||
                    !coordinates.Select(item => item.CanonicalReference).SequenceEqual(
                        table.OrderedSelectors.Select(item => item.CanonicalReference),
                        StringComparer.Ordinal))
                {
                    tableInvalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupInvalidDefinition,
                        "Every computed-selector lookup entry must name both canonical selectors exactly once.",
                        table.ComputedSelectorIndexedLookupTableId));
                    continue;
                }

                var coordinateInvalid = false;
                for (var index = 0; index < coordinates.Count; index++)
                {
                    if (coordinates[index].Index < BigInteger.Zero ||
                        coordinates[index].Index >= table.OrderedSelectors[index].Extent)
                    {
                        coordinateInvalid = true;
                        tableInvalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateLookupSelectorOutOfRange,
                            "A computed-selector lookup tuple coordinate is outside its selector extent.",
                            table.ComputedSelectorIndexedLookupTableId));
                    }
                }

                if (entry.Value <= BigInteger.Zero)
                {
                    tableInvalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateLookupValueInvalid,
                        "Computed-selector lookup values must be strictly positive exact integers.",
                        table.ComputedSelectorIndexedLookupTableId));
                }

                if (coordinateInvalid) continue;
                var tuple = TupleKey(coordinates.Select(item => item.Index));
                if (!tupleValues.TryAdd(tuple, entry.Value))
                {
                    tableInvalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupDuplicateTuple,
                        "Computed-selector lookup tuples must be unique.",
                        table.ComputedSelectorIndexedLookupTableId));
                }
            }

            var expectedCount = table.OrderedSelectors.Aggregate(
                BigInteger.One,
                (current, selector) => current * selector.Extent);
            if (expectedCount != table.OrderedEntries.Count || expectedCount != tupleValues.Count)
            {
                tableInvalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupMissingTuple,
                    "Computed-selector lookup entries must densely cover the selector Cartesian product.",
                    table.ComputedSelectorIndexedLookupTableId));
            }

            if (!tableInvalid)
            {
                model.LookupTables.Add(
                    table.ComputedSelectorIndexedLookupTableId,
                    new ValidatedLookup(table, tupleValues));
            }
            else
            {
                invalid = true;
            }
        }

        ValidateLookupGuardReferences(plan, model, diagnostics, ref invalid);

        var constraintKeys = new HashSet<string>(StringComparer.Ordinal);
        var constraintIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constraint in plan.OrderedConstraints)
        {
            if (string.IsNullOrWhiteSpace(constraint.Key) || !constraintKeys.Add(constraint.Key) ||
                !constraintIds.Add(constraint.ComputedSelectorIndexedStateConstraintId) ||
                !IsSupportedComparison(constraint.ComparisonKind) ||
                !model.StateDefinitions.TryGetValue(constraint.SubjectStateDefinitionId, out var subject) ||
                !model.LookupTables.TryGetValue(constraint.LookupTableId, out var lookup) ||
                (lookup is not null && !constraint.OrderedSelectors.Select(item => item.CanonicalReference).SequenceEqual(
                    lookup.Table.OrderedSelectors.Select(item => item.CanonicalReference),
                    StringComparer.Ordinal)))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintInvalidDefinition,
                    "A computed-selector lookup-bound constraint contains invalid, duplicate or unresolved authority.",
                    constraint.ComputedSelectorIndexedStateConstraintId));
            }
            else if (lookup!.TupleValues.Values.Any(value => value > subject!.CycleLength))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupValueInvalid,
                    "Computed-selector lookup values cannot exceed the constrained state's capacity.",
                    constraint.LookupTableId));
            }

            model.Constraints.Add(constraint);
        }

        ValidateUnconditionalTransitions(plan, model, diagnostics, ref invalid);
        ValidateEffects(plan, model, diagnostics, ref invalid, ref unsupported);
        ValidateChoices(plan, model, diagnostics, ref invalid, ref unsupported);

        if (model.ChoicesByEvent.Count == 0 || model.LookupTables.Count == 0 || model.Constraints.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid,
                "The computed guarded profile requires lookup, constraint and choice authority."));
        }

        foreach (var eventId in model.ChoicesByEvent.Keys)
        {
            model.EventDefinitionIds.Add(eventId);
            if (!model.UnconditionalTransitionsByEvent.ContainsKey(eventId))
            {
                model.UnconditionalTransitionsByEvent.Add(eventId, Array.Empty<IndexedStateTransitionDefinition>());
            }
        }

        foreach (var eventId in model.UnconditionalTransitionsByEvent.Keys)
        {
            model.EventDefinitionIds.Add(eventId);
            if (!model.ChoicesByEvent.ContainsKey(eventId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedEventUnsupported,
                    "Every computed guarded-plan event requires exactly one guarded choice.", eventId));
            }
        }

        return model;
    }

    private static void ValidateUnconditionalTransitions(
        ComputedGuardedIndexedStateTransitionPlan plan,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transition in plan.OrderedUnconditionalTransitions)
        {
            if (string.IsNullOrWhiteSpace(transition.Key) ||
                string.IsNullOrWhiteSpace(transition.SourcePeriodicEventDefinitionId) ||
                !model.StateDefinitions.ContainsKey(transition.TargetCyclicStateDefinitionId) ||
                !keys.Add(transition.Key) || !ids.Add(transition.IndexedStateTransitionDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateInvalidTransition,
                    "An unconditional transition contains invalid or duplicate authority.",
                    transition.IndexedStateTransitionDefinitionId));
            }

            var sourceTarget = transition.SourcePeriodicEventDefinitionId + "\u001f" +
                transition.TargetCyclicStateDefinitionId;
            if (!targets.Add(sourceTarget)) AddTargetConflict(transition.TargetCyclicStateDefinitionId, diagnostics, ref invalid);
            model.AddUnconditional(transition);
        }
    }

    private static void ValidateGuards(
        ComputedGuardedIndexedStateTransitionPlan plan,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid,
        ref bool unsupported)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var guard in plan.OrderedGuards)
        {
            if (string.IsNullOrWhiteSpace(guard.Key) || !keys.Add(guard.Key) ||
                !model.Guards.TryAdd(guard.IndexedStateGuardId, guard))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                    "Computed guarded-plan guard keys and IDs must be non-empty and unique.",
                    guard.IndexedStateGuardId));
            }

            switch (guard)
            {
                case ComputedSelectorIndexedLookupComparisonGuard lookupGuard:
                    if (!IsSupportedComparison(lookupGuard.ComparisonKind) ||
                        !model.StateDefinitions.ContainsKey(lookupGuard.SubjectStateDefinitionId) ||
                        lookupGuard.OrderedSelectors.Count != 2)
                    {
                        invalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                            "A computed-selector lookup comparison guard has invalid subject, comparison or selector shape.",
                            guard.IndexedStateGuardId));
                    }

                    break;
                case IndexedStateModuloComparisonGuard moduloGuard:
                    if (!StringComparer.Ordinal.Equals(
                            moduloGuard.SemanticProfile,
                            ComputedGuardedIndexedStateContract.ModuloGuardSemanticProfile) ||
                        !model.StateDefinitions.ContainsKey(moduloGuard.SubjectStateDefinitionId) ||
                        moduloGuard.Modulus <= BigInteger.Zero ||
                        moduloGuard.Residue < BigInteger.Zero ||
                        moduloGuard.Residue >= moduloGuard.Modulus ||
                        !IsSupportedModuloComparison(moduloGuard.ComparisonKind))
                    {
                        invalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateModuloGuardInvalid,
                            "A modulo guard requires a resolved state, positive modulus, canonical residue and supported comparison.",
                            guard.IndexedStateGuardId));
                    }

                    break;
                case IndexedStateConstantComparisonGuard constantGuard:
                    if (!IsSupportedComparison(constantGuard.ComparisonKind) ||
                        !model.StateDefinitions.ContainsKey(constantGuard.SubjectStateDefinitionId))
                    {
                        invalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                            "A constant comparison guard contains unresolved authority.",
                            guard.IndexedStateGuardId));
                    }

                    break;
                case IndexedStateAllOfGuard:
                    break;
                default:
                    unsupported = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                        "The bounded composite guard kind is unsupported.",
                        guard.IndexedStateGuardId));
                    break;
            }
        }

        foreach (var allOf in plan.OrderedGuards.OfType<IndexedStateAllOfGuard>())
        {
            if (allOf.OrderedChildren.Count < 2 ||
                allOf.OrderedChildren.Select(item => item.IndexedStateGuardId).Distinct(StringComparer.Ordinal).Count() !=
                allOf.OrderedChildren.Count)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                    "A flat AllOf guard requires at least two unique children.",
                    allOf.IndexedStateGuardId));
            }

            foreach (var child in allOf.OrderedChildren)
            {
                if (child is IndexedStateAllOfGuard)
                {
                    unsupported = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateGuardUnsupportedNesting,
                        "Nested AllOf guards are outside the bounded profile.",
                        allOf.IndexedStateGuardId));
                }
                else if (!model.Guards.ContainsKey(child.IndexedStateGuardId))
                {
                    invalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                        "An AllOf child is not present in the plan guard registry.",
                        child.IndexedStateGuardId));
                }
            }
        }
    }

    private static void ValidateComputedDefinitions(
        ComputedGuardedIndexedStateTransitionPlan plan,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid,
        ref bool unsupported)
    {
        var definitionKeys = new HashSet<string>(StringComparer.Ordinal);
        var caseKeys = new HashSet<string>(StringComparer.Ordinal);
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in plan.OrderedComputedDefinitions)
        {
            var definitionInvalid =
                string.IsNullOrWhiteSpace(definition.Key) ||
                !definitionKeys.Add(definition.Key) ||
                model.ComputedDefinitions.ContainsKey(definition.ComputedIndexedValueDefinitionId) ||
                definition.OutputCardinality <= BigInteger.Zero ||
                definition.OrderedCases.Count == 0 ||
                !StringComparer.Ordinal.Equals(
                    definition.SemanticProfile,
                    ComputedGuardedIndexedStateContract.ComputedDefinitionSemanticProfile);
            if (definition.MatchPolicy != GuardedStateTransitionMatchPolicy.ExactlyOne)
            {
                unsupported = true;
                definitionInvalid = true;
            }

            foreach (var computedCase in definition.OrderedCases)
            {
                if (string.IsNullOrWhiteSpace(computedCase.Key) ||
                    !caseKeys.Add(computedCase.Key) ||
                    !caseIds.Add(computedCase.ComputedIndexedValueCaseId) ||
                    computedCase.OutputIndex < BigInteger.Zero ||
                    computedCase.OutputIndex >= definition.OutputCardinality ||
                    !model.Guards.ContainsKey(computedCase.Guard.IndexedStateGuardId) ||
                    !IsClassificationGuard(computedCase.Guard))
                {
                    definitionInvalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateComputedDefinitionInvalid,
                        "A computed case has invalid authority, output range, guard reference or recursive selector dependency.",
                        computedCase.ComputedIndexedValueCaseId));
                }
            }

            if (definitionInvalid)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateComputedDefinitionInvalid,
                    "A computed indexed value requires unique authority, positive cardinality and ExactlyOne finite cases.",
                    definition.ComputedIndexedValueDefinitionId));
            }
            else
            {
                model.ComputedDefinitions.Add(definition.ComputedIndexedValueDefinitionId, definition);
            }
        }

        if (model.ComputedDefinitions.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateComputedDefinitionInvalid,
                "The computed guarded profile requires at least one computed indexed value definition."));
        }
    }

    private static bool IsClassificationGuard(BoundedIndexedStateGuardDefinition guard) =>
        guard switch
        {
            IndexedStateModuloComparisonGuard => true,
            IndexedStateConstantComparisonGuard => true,
            IndexedStateAllOfGuard allOf => allOf.OrderedChildren.All(child =>
                child is IndexedStateModuloComparisonGuard or IndexedStateConstantComparisonGuard),
            _ => false,
        };

    private static void ValidateLookupGuardReferences(
        ComputedGuardedIndexedStateTransitionPlan plan,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        foreach (var guard in plan.OrderedGuards.OfType<ComputedSelectorIndexedLookupComparisonGuard>())
        {
            if (!model.LookupTables.TryGetValue(guard.LookupTableId, out var lookup) ||
                !guard.OrderedSelectors.Select(item => item.CanonicalReference).SequenceEqual(
                    lookup.Table.OrderedSelectors.Select(item => item.CanonicalReference),
                    StringComparer.Ordinal))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                    "A computed-selector lookup guard contains unresolved or mismatched selector authority.",
                    guard.IndexedStateGuardId));
            }
        }
    }

    private static void ValidateEffects(
        ComputedGuardedIndexedStateTransitionPlan plan,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid,
        ref bool unsupported)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in plan.OrderedEffects)
        {
            if (string.IsNullOrWhiteSpace(effect.Key) || !keys.Add(effect.Key) ||
                !model.Effects.TryAdd(effect.GuardedStateEffectId, effect) ||
                !model.StateDefinitions.TryGetValue(effect.TargetStateDefinitionId, out var target))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateEffectInvalidDefinition,
                    "A computed guarded effect contains invalid, duplicate or unresolved authority.",
                    effect.GuardedStateEffectId));
                continue;
            }

            if (effect.EffectKind != GuardedStateEffectKind.AddModulo && effect.EffectKind != GuardedStateEffectKind.SetIndex)
            {
                unsupported = true;
                diagnostics.Add(Error(DiagnosticCodes.StateEffectInvalidDefinition,
                    "The computed guarded effect kind is unsupported.",
                    effect.GuardedStateEffectId));
            }
            else if (effect.EffectKind == GuardedStateEffectKind.SetIndex &&
                     (effect.Operand < BigInteger.Zero || effect.Operand >= target!.CycleLength))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateEffectInvalidIndex,
                    "SetIndex operand is outside the target state's canonical range.",
                    effect.GuardedStateEffectId));
            }
        }
    }

    private static void ValidateChoices(
        ComputedGuardedIndexedStateTransitionPlan plan,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid,
        ref bool unsupported)
    {
        var choiceKeys = new HashSet<string>(StringComparer.Ordinal);
        var choiceIds = new HashSet<string>(StringComparer.Ordinal);
        var branchKeys = new HashSet<string>(StringComparer.Ordinal);
        var branchIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in plan.OrderedGuardedChoices)
        {
            if (string.IsNullOrWhiteSpace(choice.Key) || string.IsNullOrWhiteSpace(choice.SourcePeriodicEventDefinitionId) ||
                !choiceKeys.Add(choice.Key) || !choiceIds.Add(choice.GuardedTransitionChoiceId) ||
                choice.OrderedBranches.Count == 0)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid,
                    "A computed guarded choice requires unique authority, a source event and branches.",
                    choice.GuardedTransitionChoiceId));
            }

            if (choice.MatchPolicy != GuardedStateTransitionMatchPolicy.ExactlyOne)
            {
                unsupported = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid,
                    "Only ExactlyOne computed guarded choices are supported.",
                    choice.GuardedTransitionChoiceId));
            }

            if (!model.ChoicesByEvent.TryAdd(choice.SourcePeriodicEventDefinitionId, choice))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedEventUnsupported,
                    "The bounded profile supports one guarded choice per event definition.",
                    choice.SourcePeriodicEventDefinitionId));
            }

            foreach (var branch in choice.OrderedBranches)
            {
                if (string.IsNullOrWhiteSpace(branch.Key) || !branchKeys.Add(branch.Key) ||
                    !branchIds.Add(branch.GuardedTransitionBranchId) || branch.OrderedEffects.Count == 0 ||
                    !model.Guards.ContainsKey(branch.Guard.IndexedStateGuardId))
                {
                    invalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid,
                        "Composite branch authority must be unique and reference a registered guard and effects.",
                        branch.GuardedTransitionBranchId));
                }

                var branchTargets = new HashSet<string>(StringComparer.Ordinal);
                foreach (var effect in branch.OrderedEffects)
                {
                    if (!model.Effects.ContainsKey(effect.GuardedStateEffectId))
                    {
                        invalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateEffectInvalidDefinition,
                            "A composite branch references an unregistered effect.",
                            effect.GuardedStateEffectId));
                    }

                    if (!branchTargets.Add(effect.TargetStateDefinitionId))
                    {
                        AddTargetConflict(effect.TargetStateDefinitionId, diagnostics, ref invalid);
                    }
                }

                if (model.UnconditionalTransitionsByEvent.TryGetValue(choice.SourcePeriodicEventDefinitionId, out var unconditional))
                {
                    foreach (var collision in unconditional
                                 .Select(item => item.TargetCyclicStateDefinitionId)
                                 .Where(branchTargets.Contains))
                    {
                        AddTargetConflict(collision, diagnostics, ref invalid);
                    }
                }
            }
        }
    }

    private static void ValidateSnapshot(
        HybridStateSnapshot snapshot,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SourceMechanismCandidateId) || string.IsNullOrWhiteSpace(snapshot.DriverId))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateInvalidSnapshot,
                "A hybrid snapshot requires source candidate and driver IDs."));
        }

        var states = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in snapshot.States)
        {
            if (!states.Add(state.StateDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateDuplicateSnapshotValue,
                    "Snapshot state definition IDs must be unique.", state.StateDefinitionId));
                continue;
            }

            if (!model.StateDefinitions.TryGetValue(state.StateDefinitionId, out var definition))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateMissingSnapshotValue,
                    "Snapshot contains a state outside the computed guarded plan.", state.StateDefinitionId));
            }
            else if (state.CurrentIndex < BigInteger.Zero || state.CurrentIndex >= definition.CycleLength)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateInvalidSnapshot,
                    "Snapshot state index is outside its canonical cycle range.", state.StateDefinitionId));
            }
        }

        foreach (var missing in model.StateDefinitions.Keys.Where(item => !states.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingSnapshotValue,
                "Snapshot is missing a computed guarded-plan state.", missing));
        }

        var cursors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cursor in snapshot.EventCursors)
        {
            if (!cursors.Add(cursor.PeriodicEventDefinitionId) || cursor.LastAppliedOrdinal < BigInteger.Zero)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateDuplicateCursor,
                    "Snapshot event cursors must be unique and non-negative.", cursor.PeriodicEventDefinitionId));
            }
        }

        foreach (var unexpected in cursors.Where(item => !model.EventDefinitionIds.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor,
                "Snapshot contains a cursor outside the computed guarded plan.", unexpected));
        }

        foreach (var missing in model.EventDefinitionIds.Where(item => !cursors.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor,
                "Snapshot is missing a computed guarded-plan event cursor.", missing));
        }
    }

    private static void ValidateEventEnvelope(
        HybridStateSnapshot snapshot,
        ContinuousEventBridgeResult eventResult,
        IEnumerable<string> transitionEventDefinitionIds,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        if (!eventResult.IsSuccess || eventResult.Status != ContinuousEventBridgeStatus.Complete ||
            !eventResult.SearchComplete || eventResult.ResultTruncated ||
            eventResult.SearchSummary.OmittedOccurrenceCount != BigInteger.Zero ||
            eventResult.SearchSummary.KnownTotalOccurrenceCount != eventResult.Occurrences.Count ||
            eventResult.SearchSummary.EmittedOccurrenceCount != eventResult.Occurrences.Count)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateEventStreamIncomplete,
                "Only a complete, non-truncated exact event stream can advance authoritative computed guarded state."));
        }

        if (!StringComparer.Ordinal.Equals(eventResult.Source.CandidateId, snapshot.SourceMechanismCandidateId))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateSourceMismatch,
                "Event source candidate does not match the initial hybrid snapshot."));
        }

        if (!StringComparer.Ordinal.Equals(eventResult.Source.DriverId, snapshot.DriverId) ||
            !StringComparer.Ordinal.Equals(eventResult.NormalizedRequest.DriverId, snapshot.DriverId))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateDriverMismatch,
                "Event source driver does not match the initial hybrid snapshot."));
        }

        if (eventResult.NormalizedRequest.PreviousRootTurns != snapshot.ExactRootTurns ||
            eventResult.NormalizedRequest.CurrentRootTurns < eventResult.NormalizedRequest.PreviousRootTurns)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateRootIntervalMismatch,
                "Event interval must start at the initial snapshot root and advance forward."));
        }

        var expected = new HashSet<string>(transitionEventDefinitionIds, StringComparer.Ordinal);
        var actual = new HashSet<string>(
            eventResult.NormalizedDefinitions.Select(item => item.PeriodicEventDefinitionId),
            StringComparer.Ordinal);
        if (actual.Count != eventResult.NormalizedDefinitions.Count)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition,
                "Event stream contains duplicate normalized definition IDs."));
        }

        foreach (var unexpected in actual.Where(item => !expected.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition,
                "Event stream contains a definition outside the computed guarded plan.", unexpected));
        }

        foreach (var missing in expected.Where(item => !actual.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition,
                "Event stream omits a computed guarded-plan event definition.", missing));
        }
    }

    private static void ValidateOccurrences(
        HybridStateSnapshot snapshot,
        ContinuousEventBridgeResult eventResult,
        ISet<string> eventDefinitionIds,
        IReadOnlyList<PeriodicEventOccurrence> occurrences,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        var occurrenceIds = new HashSet<string>(StringComparer.Ordinal);
        var definitionOrdinals = new HashSet<string>(StringComparer.Ordinal);
        var cursors = snapshot.EventCursors.ToDictionary(
            item => item.PeriodicEventDefinitionId,
            item => item.LastAppliedOrdinal,
            StringComparer.Ordinal);
        foreach (var occurrence in occurrences)
        {
            var signature = occurrence.PeriodicEventDefinitionId + "\u001f" + occurrence.EventOrdinal;
            if (!occurrenceIds.Add(occurrence.OccurrenceId) || !definitionOrdinals.Add(signature))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceDuplicate,
                    "The event stream contains a duplicate occurrence.", occurrence.OccurrenceId));
            }

            if (!eventDefinitionIds.Contains(occurrence.PeriodicEventDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition,
                    "Occurrence has no matching computed guarded choice.", occurrence.PeriodicEventDefinitionId));
                continue;
            }

            if (!cursors.TryGetValue(occurrence.PeriodicEventDefinitionId, out var lastAppliedOrdinal))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor,
                    "Occurrence event definition has no snapshot cursor.", occurrence.PeriodicEventDefinitionId));
                continue;
            }

            var expectedId = ContinuousEventBridgeIdentity.ComputeOccurrenceId(
                occurrence.PeriodicEventDefinitionId,
                occurrence.EventOrdinal,
                occurrence.TraversalDirection);
            if (!StringComparer.Ordinal.Equals(expectedId, occurrence.OccurrenceId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceIdentityMismatch,
                    "Occurrence identity does not match definition, ordinal and direction.", occurrence.OccurrenceId));
            }

            if (occurrence.TraversalDirection != PeriodicEventTraversalDirection.Forward ||
                !StringComparer.Ordinal.Equals(occurrence.DriverId, snapshot.DriverId) ||
                !(eventResult.NormalizedRequest.PreviousRootTurns < occurrence.RootTurnsAtCrossing &&
                  occurrence.RootTurnsAtCrossing <= eventResult.NormalizedRequest.CurrentRootTurns))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceIntervalMismatch,
                    "Occurrence lies outside the supported forward event interval.", occurrence.OccurrenceId));
            }

            var expectedOrdinal = lastAppliedOrdinal + BigInteger.One;
            if (occurrence.EventOrdinal < expectedOrdinal)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceStale,
                    "Occurrence ordinal is duplicate or stale for the snapshot cursor.", occurrence.OccurrenceId));
            }
            else if (occurrence.EventOrdinal > expectedOrdinal)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceGap,
                    "Occurrence ordinal is not contiguous with the snapshot cursor.", occurrence.OccurrenceId));
            }

            cursors[occurrence.PeriodicEventDefinitionId] = occurrence.EventOrdinal;
        }
    }

    private static bool ValidateConstraints(
        IReadOnlyDictionary<string, BigInteger> stateValues,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        var valid = true;
        var computedSelections = EvaluateComputedDefinitions(stateValues, model, diagnostics, ref invalid);
        if (invalid) return false;
        foreach (var constraint in model.Constraints)
        {
            if (!TryLookup(
                    constraint.LookupTableId,
                    stateValues,
                    computedSelections,
                    model,
                    diagnostics,
                    ref invalid,
                    out var bound))
            {
                valid = false;
                continue;
            }

            if (!stateValues.TryGetValue(constraint.SubjectStateDefinitionId, out var subject) ||
                !Compare(subject, bound, constraint.ComparisonKind))
            {
                valid = false;
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintViolation,
                    "The hybrid state violates a computed-selector lookup-bound constraint.",
                    constraint.ComputedSelectorIndexedStateConstraintId));
            }
        }

        return valid;
    }

    private static bool EvaluateGuard(
        BoundedIndexedStateGuardDefinition guard,
        IReadOnlyDictionary<string, BigInteger> preState,
        IReadOnlyDictionary<string, ComputedSelection> computedSelections,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        switch (guard)
        {
            case ComputedSelectorIndexedLookupComparisonGuard lookupGuard:
                if (!TryLookup(
                        lookupGuard.LookupTableId,
                        preState,
                        computedSelections,
                        model,
                        diagnostics,
                        ref invalid,
                        out var right) ||
                    !preState.TryGetValue(lookupGuard.SubjectStateDefinitionId, out var lookupSubject))
                {
                    invalid = true;
                    return false;
                }

                return Compare(lookupSubject + lookupGuard.SignedSubjectOffset, right, lookupGuard.ComparisonKind);
            case IndexedStateConstantComparisonGuard constantGuard:
                if (!preState.TryGetValue(constantGuard.SubjectStateDefinitionId, out var constantSubject))
                {
                    invalid = true;
                    return false;
                }

                return Compare(
                    constantSubject + constantGuard.SignedSubjectOffset,
                    constantGuard.Constant,
                    constantGuard.ComparisonKind);
            case IndexedStateModuloComparisonGuard moduloGuard:
                if (!preState.TryGetValue(moduloGuard.SubjectStateDefinitionId, out var moduloSubject) ||
                    moduloGuard.Modulus <= BigInteger.Zero ||
                    moduloGuard.Residue < BigInteger.Zero ||
                    moduloGuard.Residue >= moduloGuard.Modulus)
                {
                    invalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateModuloGuardInvalid,
                        "A modulo guard cannot resolve a canonical subject, modulus and residue.",
                        moduloGuard.IndexedStateGuardId));
                    return false;
                }

                var normalized = EuclideanModulo(moduloSubject, moduloGuard.Modulus);
                return moduloGuard.ComparisonKind switch
                {
                    IndexedStateModuloComparisonKind.Equal => normalized == moduloGuard.Residue,
                    IndexedStateModuloComparisonKind.NotEqual => normalized != moduloGuard.Residue,
                    _ => false,
                };
            case IndexedStateAllOfGuard allOf:
                var all = true;
                foreach (var child in allOf.OrderedChildren)
                {
                    if (!model.Guards.TryGetValue(child.IndexedStateGuardId, out var registered))
                    {
                        invalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                            "An AllOf child is not registered in the computed guarded plan.",
                            child.IndexedStateGuardId));
                        return false;
                    }

                    all &= EvaluateGuard(
                        registered,
                        preState,
                        computedSelections,
                        model,
                        diagnostics,
                        ref invalid);
                }

                return all;
            default:
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition,
                    "The computed guarded plan contains an unknown guard kind.", guard.IndexedStateGuardId));
                return false;
        }
    }

    private static Dictionary<string, ComputedSelection> EvaluateComputedDefinitions(
        IReadOnlyDictionary<string, BigInteger> preState,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        var selections = new Dictionary<string, ComputedSelection>(StringComparer.Ordinal);
        foreach (var definition in model.ComputedDefinitions.Values
                     .OrderBy(item => item.ComputedIndexedValueDefinitionId, StringComparer.Ordinal))
        {
            var noComputedValues = new Dictionary<string, ComputedSelection>(StringComparer.Ordinal);
            var matching = new List<ComputedIndexedValueCase>();
            foreach (var computedCase in definition.OrderedCases)
            {
                if (EvaluateGuard(
                        computedCase.Guard,
                        preState,
                        noComputedValues,
                        model,
                        diagnostics,
                        ref invalid))
                {
                    matching.Add(computedCase);
                }
            }
            if (invalid) return selections;
            if (matching.Count == 0)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateComputedNoMatch,
                    "ExactlyOne computed indexed classification had no matching case.",
                    definition.ComputedIndexedValueDefinitionId));
                return selections;
            }

            if (matching.Count != 1)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateComputedAmbiguous,
                    "ExactlyOne computed indexed classification had more than one matching case.",
                    definition.ComputedIndexedValueDefinitionId));
                return selections;
            }

            var selected = matching[0];
            selections.Add(
                definition.ComputedIndexedValueDefinitionId,
                new ComputedSelection(definition, selected, selected.OutputIndex));
        }

        return selections;
    }

    private static bool TryLookup(
        string lookupTableId,
        IReadOnlyDictionary<string, BigInteger> stateValues,
        IReadOnlyDictionary<string, ComputedSelection> computedSelections,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid,
        out BigInteger value)
    {
        value = BigInteger.Zero;
        if (!model.LookupTables.TryGetValue(lookupTableId, out var lookup))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupInvalidDefinition,
                "Computed-selector lookup authority is missing.", lookupTableId));
            return false;
        }

        var indexes = new List<BigInteger>();
        foreach (var selector in lookup.Table.OrderedSelectors)
        {
            BigInteger index;
            var resolved = selector.SelectorKind switch
            {
                ComputedIndexedSelectorKind.State => stateValues.TryGetValue(selector.ReferenceId, out index),
                ComputedIndexedSelectorKind.ComputedIndexedValue =>
                    computedSelections.TryGetValue(selector.ReferenceId, out var selection) &&
                    Assign(selection.OutputIndex, out index),
                _ => Assign(BigInteger.MinusOne, out index) && false,
            };
            if (!resolved || index < BigInteger.Zero || index >= selector.Extent)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupSelectorOutOfRange,
                    "Computed-selector lookup selector is outside its canonical range.", lookupTableId));
                return false;
            }

            indexes.Add(index);
        }

        if (!lookup.TupleValues.TryGetValue(TupleKey(indexes), out value) || value <= BigInteger.Zero)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupMissingTuple,
                "Computed-selector lookup has no positive value for the selected tuple.", lookupTableId));
            return false;
        }

        return true;
    }

    private static bool Assign(BigInteger value, out BigInteger target)
    {
        target = value;
        return true;
    }

    private static bool Compare(BigInteger left, BigInteger right, IndexedStateLookupComparisonKind comparisonKind) =>
        comparisonKind switch
        {
            IndexedStateLookupComparisonKind.LessThan => left < right,
            IndexedStateLookupComparisonKind.Equal => left == right,
            _ => false,
        };

    private static bool IsSupportedComparison(IndexedStateLookupComparisonKind value) =>
        value == IndexedStateLookupComparisonKind.LessThan || value == IndexedStateLookupComparisonKind.Equal;

    private static bool IsSupportedModuloComparison(IndexedStateModuloComparisonKind value) =>
        value == IndexedStateModuloComparisonKind.Equal || value == IndexedStateModuloComparisonKind.NotEqual;

    private static Dictionary<string, BigInteger> ToStateDictionary(HybridStateSnapshot snapshot) =>
        snapshot.States.ToDictionary(item => item.StateDefinitionId, item => item.CurrentIndex, StringComparer.Ordinal);

    private static string TupleKey(IEnumerable<BigInteger> indexes) =>
        string.Join("\u001f", indexes.Select(item => item.ToString(CultureInfo.InvariantCulture)));

    private static void AddTargetConflict(string targetId, ICollection<Diagnostic> diagnostics, ref bool invalid)
    {
        invalid = true;
        diagnostics.Add(Error(DiagnosticCodes.StateEffectTargetConflict,
            "An occurrence cannot update the same target state more than once.", targetId));
    }

    private static HybridStateSnapshot CreateSnapshot(
        HybridStateSnapshot source,
        Rational rootTurns,
        IReadOnlyDictionary<string, BigInteger> states,
        IReadOnlyDictionary<string, BigInteger> cursors) =>
        new(
            source.SourceMechanismCandidateId,
            source.DriverId,
            rootTurns,
            states.Select(item => new CyclicIndexedStateValue(item.Key, item.Value)),
            cursors.Select(item => new HybridEventCursor(item.Key, item.Value)));

    private static ComputedGuardedHybridStateTransitionResult Terminal(
        ContinuousEventBridgeResult eventResult,
        ComputedGuardedIndexedStateTransitionPlan plan,
        GuardedHybridStateAdvanceRequest request,
        string requestId,
        GuardedHybridStateTransitionStatus status,
        HybridStateSnapshot initialSnapshot,
        IEnumerable<Diagnostic> diagnostics,
        GuardedHybridStateTransitionPerformance performance) =>
        new(
            eventResult,
            plan,
            request,
            requestId,
            status,
            false,
            false,
            new GuardedHybridStateTransitionSummary(
                eventResult.Occurrences.Count,
                BigInteger.Zero,
                eventResult.Occurrences.Count,
                BigInteger.Zero,
                BigInteger.Zero),
            initialSnapshot,
            Array.Empty<StateTransitionApplication>(),
            Array.Empty<GuardedStateEffectApplication>(),
            Array.Empty<ComputedIndexedValueEvaluation>(),
            Array.Empty<GuardedOccurrenceApplicationGroup>(),
            null,
            null,
            diagnostics,
            performance);

    private static BigInteger EuclideanModulo(BigInteger value, BigInteger modulus)
    {
        var remainder = value % modulus;
        return remainder.Sign < 0 ? remainder + modulus : remainder;
    }

    private static long ToMicroseconds(long elapsedTicks) =>
        (long)((decimal)elapsedTicks * 1_000_000m / Stopwatch.Frequency);

    private static Diagnostic Error(string code, string message, string? subjectId = null) =>
        new(code, DiagnosticSeverity.Error, message, subjectId);

    private sealed class ValidatedLookup
    {
        public ValidatedLookup(
            ComputedSelectorIndexedLookupTableDefinition table,
            Dictionary<string, BigInteger> tupleValues)
        {
            Table = table;
            TupleValues = tupleValues;
        }

        public ComputedSelectorIndexedLookupTableDefinition Table { get; }
        public Dictionary<string, BigInteger> TupleValues { get; }
    }

    private sealed class ComputedSelection
    {
        public ComputedSelection(
            ComputedIndexedValueDefinition definition,
            ComputedIndexedValueCase computedCase,
            BigInteger outputIndex)
        {
            Definition = definition;
            Case = computedCase;
            OutputIndex = outputIndex;
        }

        public ComputedIndexedValueDefinition Definition { get; }
        public ComputedIndexedValueCase Case { get; }
        public BigInteger OutputIndex { get; }
    }

    private sealed class ValidatedModel
    {
        public Dictionary<string, CyclicIndexedStateDefinition> StateDefinitions { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, ValidatedLookup> LookupTables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ComputedIndexedValueDefinition> ComputedDefinitions { get; } =
            new(StringComparer.Ordinal);
        public List<ComputedSelectorIndexedLookupBoundConstraint> Constraints { get; } = new();
        public Dictionary<string, BoundedIndexedStateGuardDefinition> Guards { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, GuardedStateEffectDefinition> Effects { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IndexedStateTransitionDefinition[]> UnconditionalTransitionsByEvent { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, ComputedGuardedStateTransitionChoice> ChoicesByEvent { get; } =
            new(StringComparer.Ordinal);
        public HashSet<string> EventDefinitionIds { get; } = new(StringComparer.Ordinal);

        public void AddUnconditional(IndexedStateTransitionDefinition transition)
        {
            if (!UnconditionalTransitionsByEvent.TryGetValue(transition.SourcePeriodicEventDefinitionId, out var existing))
            {
                UnconditionalTransitionsByEvent.Add(
                    transition.SourcePeriodicEventDefinitionId,
                    new[] { transition });
                return;
            }

            UnconditionalTransitionsByEvent[transition.SourcePeriodicEventDefinitionId] = existing
                .Concat(new[] { transition })
                .OrderBy(item => item.IndexedStateTransitionDefinitionId, StringComparer.Ordinal)
                .ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class GuardedIndexedStateTransitionEngine
{
    public GuardedHybridStateTransitionResult Apply(
        HybridStateSnapshot initialSnapshot,
        GuardedIndexedStateTransitionPlan transitionPlan,
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
        var requestId = GuardedHybridStateTransitionIdentity.ComputeAdvanceRequestId(
            initialSnapshot,
            transitionPlan,
            eventResult.EventBridgeRequestId,
            request);

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                GuardedHybridStateTransitionContract.DefaultDeterminismProfile) ||
            !StringComparer.Ordinal.Equals(
                transitionPlan.DeterminismProfile,
                GuardedHybridStateTransitionContract.DefaultDeterminismProfile))
        {
            unsupported = true;
            diagnostics.Add(Error(
                DiagnosticCodes.StateGuardedUnsupportedProfile,
                "The guarded indexed-state determinism profile is unsupported."));
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
            ValidateConstraints(initialSnapshot.States.ToDictionary(
                item => item.StateDefinitionId,
                item => item.CurrentIndex,
                StringComparer.Ordinal), model, diagnostics, ref invalid);
        }

        validationWatch.Stop();
        if (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateGuardedCancelled,
                "Guarded indexed-state transition was cancelled before occurrence application."));
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

        var stateValues = initialSnapshot.States.ToDictionary(
            item => item.StateDefinitionId,
            item => item.CurrentIndex,
            StringComparer.Ordinal);
        var cursors = initialSnapshot.EventCursors.ToDictionary(
            item => item.PeriodicEventDefinitionId,
            item => item.LastAppliedOrdinal,
            StringComparer.Ordinal);
        var unconditionalApplications = new List<StateTransitionApplication>();
        var guardedApplications = new List<GuardedStateEffectApplication>();
        var groups = new List<GuardedOccurrenceApplicationGroup>();
        var appliedOccurrenceCount = 0;
        var guardTicks = 0L;
        var effectTicks = 0L;
        var commitTicks = 0L;
        Rational checkpointRoot = initialSnapshot.ExactRootTurns;
        var cancelled = false;
        var maximum = Math.Min(request.MaxAppliedOccurrences, occurrences.Count);

        for (var occurrenceIndex = 0; occurrenceIndex < maximum; occurrenceIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateGuardedCancelled,
                    "Guarded indexed-state transition observed cancellation between occurrence groups."));
                break;
            }

            var occurrence = occurrences[occurrenceIndex];
            if (!ValidateConstraints(stateValues, model, diagnostics, ref invalid))
            {
                break;
            }

            var guardWatch = Stopwatch.StartNew();
            var choice = model.ChoicesByEvent[occurrence.PeriodicEventDefinitionId];
            var matchingBranches = choice.OrderedBranches
                .Where(branch => EvaluateGuard(branch.Guard, stateValues, model, diagnostics, ref invalid))
                .ToList();
            guardWatch.Stop();
            guardTicks += guardWatch.ElapsedTicks;
            if (invalid)
            {
                break;
            }

            if (matchingBranches.Count == 0)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateGuardNoMatch,
                    "ExactlyOne guarded choice had no matching branch.",
                    choice.GuardedTransitionChoiceId));
                break;
            }

            if (matchingBranches.Count != 1)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateGuardAmbiguous,
                    "ExactlyOne guarded choice had more than one matching branch.",
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
                                "The guarded effect kind is unsupported.",
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
            foreach (var update in pendingValues)
            {
                pendingState[update.Key] = update.Value;
            }

            if (!invalid && !unsupported)
            {
                ValidateConstraints(pendingState, model, diagnostics, ref invalid);
            }

            effectWatch.Stop();
            effectTicks += effectWatch.ElapsedTicks;
            if (invalid || unsupported)
            {
                break;
            }

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
            foreach (var update in pendingValues)
            {
                stateValues[update.Key] = update.Value;
            }

            unconditionalApplications.AddRange(pendingUnconditional);
            guardedApplications.AddRange(pendingGuarded);
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
        return new GuardedHybridStateTransitionResult(
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
        GuardedIndexedStateTransitionPlan plan,
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
                diagnostics.Add(Error(DiagnosticCodes.StateInvalidDefinition, "A guarded plan state requires a stable key and positive cycle length.", state.CyclicStateDefinitionId));
            }

            if (!stateKeys.Add(state.Key) || !model.StateDefinitions.TryAdd(state.CyclicStateDefinitionId, state))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateDuplicateDefinition, "Guarded plan state keys and IDs must be unique.", state.CyclicStateDefinitionId));
            }
        }

        if (model.StateDefinitions.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid, "A guarded plan requires at least one state."));
        }

        var tableKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in plan.OrderedLookupTables)
        {
            if (string.IsNullOrWhiteSpace(table.Key) || string.IsNullOrWhiteSpace(table.SelectorStateDefinitionId) ||
                !StringComparer.Ordinal.Equals(table.SemanticProfile, GuardedIndexedStateContract.LookupSemanticProfile))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupInvalidDefinition, "A lookup table requires a stable key, selector and supported exact-integer profile.", table.IndexedStateLookupTableId));
            }

            if (!tableKeys.Add(table.Key) || !model.LookupTables.TryAdd(table.IndexedStateLookupTableId, table))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupDuplicateDefinition, "Lookup table keys and IDs must be unique.", table.IndexedStateLookupTableId));
            }

            if (!model.StateDefinitions.TryGetValue(table.SelectorStateDefinitionId, out var selector) ||
                selector.CycleLength != table.Values.Count)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupInvalidDefinition, "Lookup value count must equal the selector state's cycle length.", table.IndexedStateLookupTableId));
            }

            if (table.Values.Count == 0 || table.Values.Any(value => value <= BigInteger.Zero))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupValueInvalid, "Lookup values must be strictly positive exact integers.", table.IndexedStateLookupTableId));
            }
        }

        var constraintKeys = new HashSet<string>(StringComparer.Ordinal);
        var constraintIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constraint in plan.OrderedConstraints)
        {
            if (string.IsNullOrWhiteSpace(constraint.Key) || !constraintKeys.Add(constraint.Key) ||
                !constraintIds.Add(constraint.IndexedStateConstraintId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintInvalidDefinition, "Constraint keys and IDs must be non-empty and unique.", constraint.IndexedStateConstraintId));
            }

            if (!IsSupportedComparison(constraint.ComparisonKind))
            {
                unsupported = true;
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintInvalidDefinition, "The lookup-bound comparison kind is unsupported.", constraint.IndexedStateConstraintId));
            }

            if (!model.StateDefinitions.TryGetValue(constraint.SubjectStateDefinitionId, out var subject) ||
                !model.StateDefinitions.ContainsKey(constraint.SelectorStateDefinitionId) ||
                !model.LookupTables.TryGetValue(constraint.LookupTableId, out var table) ||
                (table is not null && !StringComparer.Ordinal.Equals(table.SelectorStateDefinitionId, constraint.SelectorStateDefinitionId)))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintInvalidDefinition, "A lookup-bound constraint contains an unresolved or inconsistent reference.", constraint.IndexedStateConstraintId));
            }
            else if (table!.Values.Any(value => value > subject!.CycleLength))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateLookupValueInvalid, "Lookup values cannot exceed the constrained state's capacity.", table.IndexedStateLookupTableId));
            }

            model.Constraints.Add(constraint);
        }

        var unconditionalIds = new HashSet<string>(StringComparer.Ordinal);
        var unconditionalKeys = new HashSet<string>(StringComparer.Ordinal);
        var unconditionalTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transition in plan.OrderedUnconditionalTransitions)
        {
            if (string.IsNullOrWhiteSpace(transition.Key) ||
                string.IsNullOrWhiteSpace(transition.SourcePeriodicEventDefinitionId) ||
                !model.StateDefinitions.ContainsKey(transition.TargetCyclicStateDefinitionId) ||
                !unconditionalKeys.Add(transition.Key) ||
                !unconditionalIds.Add(transition.IndexedStateTransitionDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateInvalidTransition, "An unconditional guarded-plan transition contains invalid or duplicate authority.", transition.IndexedStateTransitionDefinitionId));
            }

            var sourceTarget = transition.SourcePeriodicEventDefinitionId + "\u001f" +
                transition.TargetCyclicStateDefinitionId;
            if (!unconditionalTargets.Add(sourceTarget))
            {
                AddTargetConflict(transition.TargetCyclicStateDefinitionId, diagnostics, ref invalid);
            }

            model.AddUnconditional(transition);
        }

        var choiceKeys = new HashSet<string>(StringComparer.Ordinal);
        var choiceIds = new HashSet<string>(StringComparer.Ordinal);
        var branchKeys = new HashSet<string>(StringComparer.Ordinal);
        var branchIds = new HashSet<string>(StringComparer.Ordinal);
        var guardKeys = new HashSet<string>(StringComparer.Ordinal);
        var guardIds = new HashSet<string>(StringComparer.Ordinal);
        var effectKeys = new HashSet<string>(StringComparer.Ordinal);
        var effectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in plan.OrderedGuardedChoices)
        {
            if (string.IsNullOrWhiteSpace(choice.Key) || string.IsNullOrWhiteSpace(choice.SourcePeriodicEventDefinitionId) ||
                !choiceKeys.Add(choice.Key) || !choiceIds.Add(choice.GuardedTransitionChoiceId) ||
                choice.OrderedBranches.Count == 0)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid, "A guarded choice requires unique authority, a source event and branches.", choice.GuardedTransitionChoiceId));
            }

            if (choice.MatchPolicy != GuardedStateTransitionMatchPolicy.ExactlyOne)
            {
                unsupported = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid, "Only ExactlyOne guarded choices are supported.", choice.GuardedTransitionChoiceId));
            }

            if (!model.ChoicesByEvent.TryAdd(choice.SourcePeriodicEventDefinitionId, choice))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedEventUnsupported, "The bounded profile supports one guarded choice per event definition.", choice.SourcePeriodicEventDefinitionId));
            }

            foreach (var branch in choice.OrderedBranches)
            {
                if (string.IsNullOrWhiteSpace(branch.Key) || !branchKeys.Add(branch.Key) ||
                    !branchIds.Add(branch.GuardedTransitionBranchId) || branch.OrderedEffects.Count == 0)
                {
                    invalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid, "Guarded branch authority must be unique and contain effects.", branch.GuardedTransitionBranchId));
                }

                var guard = branch.Guard;
                if (string.IsNullOrWhiteSpace(guard.Key) || !guardKeys.Add(guard.Key) ||
                    !guardIds.Add(guard.IndexedStateGuardId) ||
                    !model.StateDefinitions.ContainsKey(guard.SubjectStateDefinitionId) ||
                    !model.StateDefinitions.ContainsKey(guard.SelectorStateDefinitionId) ||
                    !model.LookupTables.TryGetValue(guard.LookupTableId, out var guardTable) ||
                    (guardTable is not null && !StringComparer.Ordinal.Equals(guardTable.SelectorStateDefinitionId, guard.SelectorStateDefinitionId)))
                {
                    invalid = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition, "A guarded lookup comparison contains invalid, duplicate or unresolved authority.", guard.IndexedStateGuardId));
                }

                if (!IsSupportedComparison(guard.ComparisonKind))
                {
                    unsupported = true;
                    diagnostics.Add(Error(DiagnosticCodes.StateGuardInvalidDefinition, "The guard comparison kind is unsupported.", guard.IndexedStateGuardId));
                }

                var branchTargets = new HashSet<string>(StringComparer.Ordinal);
                foreach (var effect in branch.OrderedEffects)
                {
                    if (string.IsNullOrWhiteSpace(effect.Key) || !effectKeys.Add(effect.Key) ||
                        !effectIds.Add(effect.GuardedStateEffectId) ||
                        !model.StateDefinitions.TryGetValue(effect.TargetStateDefinitionId, out var target))
                    {
                        invalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateEffectInvalidDefinition, "A guarded effect contains invalid, duplicate or unresolved authority.", effect.GuardedStateEffectId));
                        continue;
                    }

                    if (effect.EffectKind != GuardedStateEffectKind.AddModulo && effect.EffectKind != GuardedStateEffectKind.SetIndex)
                    {
                        unsupported = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateEffectInvalidDefinition, "The guarded effect kind is unsupported.", effect.GuardedStateEffectId));
                    }
                    else if (effect.EffectKind == GuardedStateEffectKind.SetIndex &&
                             (effect.Operand < BigInteger.Zero || effect.Operand >= target!.CycleLength))
                    {
                        invalid = true;
                        diagnostics.Add(Error(DiagnosticCodes.StateEffectInvalidIndex, "SetIndex operand is outside the target state's canonical range.", effect.GuardedStateEffectId));
                    }

                    if (!branchTargets.Add(effect.TargetStateDefinitionId))
                    {
                        AddTargetConflict(effect.TargetStateDefinitionId, diagnostics, ref invalid);
                    }
                }

                if (model.UnconditionalTransitionsByEvent.TryGetValue(choice.SourcePeriodicEventDefinitionId, out var unconditional))
                {
                    foreach (var collision in unconditional.Select(item => item.TargetCyclicStateDefinitionId).Where(branchTargets.Contains))
                    {
                        AddTargetConflict(collision, diagnostics, ref invalid);
                    }
                }
            }
        }

        if (model.ChoicesByEvent.Count == 0 || model.LookupTables.Count == 0 || model.Constraints.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateGuardedPlanInvalid, "The bounded guarded profile requires lookup, constraint and choice authority."));
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
                diagnostics.Add(Error(DiagnosticCodes.StateGuardedEventUnsupported, "Every bounded guarded-plan event requires exactly one guarded choice.", eventId));
            }
        }

        return model;
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
            diagnostics.Add(Error(DiagnosticCodes.StateInvalidSnapshot, "A hybrid snapshot requires source candidate and driver IDs."));
        }

        var states = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in snapshot.States)
        {
            if (!states.Add(state.StateDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateDuplicateSnapshotValue, "Snapshot state definition IDs must be unique.", state.StateDefinitionId));
                continue;
            }

            if (!model.StateDefinitions.TryGetValue(state.StateDefinitionId, out var definition))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateMissingSnapshotValue, "Snapshot contains a state outside the guarded plan.", state.StateDefinitionId));
            }
            else if (state.CurrentIndex < BigInteger.Zero || state.CurrentIndex >= definition.CycleLength)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateInvalidSnapshot, "Snapshot state index is outside its canonical cycle range.", state.StateDefinitionId));
            }
        }

        foreach (var missing in model.StateDefinitions.Keys.Where(item => !states.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingSnapshotValue, "Snapshot is missing a guarded-plan state.", missing));
        }

        var cursors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cursor in snapshot.EventCursors)
        {
            if (!cursors.Add(cursor.PeriodicEventDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateDuplicateCursor, "Snapshot event cursor IDs must be unique.", cursor.PeriodicEventDefinitionId));
            }
        }

        foreach (var unexpected in cursors.Where(item => !model.EventDefinitionIds.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor, "Snapshot contains a cursor outside the guarded plan.", unexpected));
        }

        foreach (var missing in model.EventDefinitionIds.Where(item => !cursors.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor, "Snapshot is missing a guarded-plan event cursor.", missing));
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
            diagnostics.Add(Error(DiagnosticCodes.StateEventStreamIncomplete, "Only a complete, non-truncated exact event stream can advance authoritative guarded state."));
        }

        if (!StringComparer.Ordinal.Equals(eventResult.Source.CandidateId, snapshot.SourceMechanismCandidateId))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateSourceMismatch, "Event source candidate does not match the initial hybrid snapshot."));
        }

        if (!StringComparer.Ordinal.Equals(eventResult.Source.DriverId, snapshot.DriverId) ||
            !StringComparer.Ordinal.Equals(eventResult.NormalizedRequest.DriverId, snapshot.DriverId))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateDriverMismatch, "Event source driver does not match the initial hybrid snapshot."));
        }

        if (eventResult.NormalizedRequest.PreviousRootTurns != snapshot.ExactRootTurns ||
            eventResult.NormalizedRequest.CurrentRootTurns < eventResult.NormalizedRequest.PreviousRootTurns)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateRootIntervalMismatch, "Event interval must start at the initial snapshot root and advance forward."));
        }

        var expected = new HashSet<string>(transitionEventDefinitionIds, StringComparer.Ordinal);
        var actual = new HashSet<string>(eventResult.NormalizedDefinitions.Select(item => item.PeriodicEventDefinitionId), StringComparer.Ordinal);
        if (actual.Count != eventResult.NormalizedDefinitions.Count)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Event stream contains duplicate normalized definition IDs."));
        }

        foreach (var unexpected in actual.Where(item => !expected.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Event stream contains a definition outside the guarded plan.", unexpected));
        }

        foreach (var missing in expected.Where(item => !actual.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Event stream omits a guarded-plan event definition.", missing));
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
        var cursors = snapshot.EventCursors.ToDictionary(item => item.PeriodicEventDefinitionId, item => item.LastAppliedOrdinal, StringComparer.Ordinal);
        foreach (var occurrence in occurrences)
        {
            var signature = occurrence.PeriodicEventDefinitionId + "\u001f" + occurrence.EventOrdinal;
            if (!occurrenceIds.Add(occurrence.OccurrenceId) || !definitionOrdinals.Add(signature))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceDuplicate, "The event stream contains a duplicate occurrence.", occurrence.OccurrenceId));
            }

            if (!eventDefinitionIds.Contains(occurrence.PeriodicEventDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Occurrence has no matching guarded choice.", occurrence.PeriodicEventDefinitionId));
                continue;
            }

            if (!cursors.TryGetValue(occurrence.PeriodicEventDefinitionId, out var lastAppliedOrdinal))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor, "Occurrence event definition has no snapshot cursor.", occurrence.PeriodicEventDefinitionId));
                continue;
            }

            var expectedId = ContinuousEventBridgeIdentity.ComputeOccurrenceId(
                occurrence.PeriodicEventDefinitionId,
                occurrence.EventOrdinal,
                occurrence.TraversalDirection);
            if (!StringComparer.Ordinal.Equals(expectedId, occurrence.OccurrenceId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceIdentityMismatch, "Occurrence identity does not match definition, ordinal and direction.", occurrence.OccurrenceId));
            }

            if (occurrence.TraversalDirection != PeriodicEventTraversalDirection.Forward ||
                !StringComparer.Ordinal.Equals(occurrence.DriverId, snapshot.DriverId) ||
                !(eventResult.NormalizedRequest.PreviousRootTurns < occurrence.RootTurnsAtCrossing &&
                  occurrence.RootTurnsAtCrossing <= eventResult.NormalizedRequest.CurrentRootTurns))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceIntervalMismatch, "Occurrence lies outside the supported forward event interval.", occurrence.OccurrenceId));
            }

            var expectedOrdinal = lastAppliedOrdinal + BigInteger.One;
            if (occurrence.EventOrdinal < expectedOrdinal)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceStale, "Occurrence ordinal is duplicate or stale for the snapshot cursor.", occurrence.OccurrenceId));
            }
            else if (occurrence.EventOrdinal > expectedOrdinal)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceGap, "Occurrence ordinal is not contiguous with the snapshot cursor.", occurrence.OccurrenceId));
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
        foreach (var constraint in model.Constraints)
        {
            if (!TryLookup(constraint.LookupTableId, constraint.SelectorStateDefinitionId, stateValues, model, diagnostics, ref invalid, out var bound))
            {
                valid = false;
                continue;
            }

            var subject = stateValues[constraint.SubjectStateDefinitionId];
            var matches = Compare(subject, bound, constraint.ComparisonKind);
            if (!matches)
            {
                valid = false;
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintViolation, "The hybrid state violates a lookup-bound constraint.", constraint.IndexedStateConstraintId));
            }
        }

        return valid;
    }

    private static bool EvaluateGuard(
        IndexedStateLookupComparisonGuard guard,
        IReadOnlyDictionary<string, BigInteger> preState,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        if (!TryLookup(guard.LookupTableId, guard.SelectorStateDefinitionId, preState, model, diagnostics, ref invalid, out var right))
        {
            return false;
        }

        var left = preState[guard.SubjectStateDefinitionId] + guard.SignedSubjectOffset;
        return Compare(left, right, guard.ComparisonKind);
    }

    private static bool TryLookup(
        string lookupTableId,
        string selectorStateDefinitionId,
        IReadOnlyDictionary<string, BigInteger> stateValues,
        ValidatedModel model,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid,
        out BigInteger value)
    {
        value = BigInteger.Zero;
        if (!model.LookupTables.TryGetValue(lookupTableId, out var table) ||
            !stateValues.TryGetValue(selectorStateDefinitionId, out var selector) ||
            selector < BigInteger.Zero || selector >= table.Values.Count)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateLookupSelectorOutOfRange, "Lookup selector is outside the table's canonical range.", lookupTableId));
            return false;
        }

        value = table.Values[(int)selector];
        if (value <= BigInteger.Zero)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateLookupValueInvalid, "Lookup produced a non-positive value.", lookupTableId));
            return false;
        }

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

    private static void AddTargetConflict(string targetId, ICollection<Diagnostic> diagnostics, ref bool invalid)
    {
        invalid = true;
        diagnostics.Add(Error(DiagnosticCodes.StateEffectTargetConflict, "An occurrence cannot update the same target state more than once.", targetId));
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

    private static GuardedHybridStateTransitionResult Terminal(
        ContinuousEventBridgeResult eventResult,
        GuardedIndexedStateTransitionPlan plan,
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

    private sealed class ValidatedModel
    {
        public Dictionary<string, CyclicIndexedStateDefinition> StateDefinitions { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, IndexedStateLookupTableDefinition> LookupTables { get; } =
            new(StringComparer.Ordinal);
        public List<IndexedStateLookupBoundConstraint> Constraints { get; } = new();
        public Dictionary<string, IndexedStateTransitionDefinition[]> UnconditionalTransitionsByEvent { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, GuardedStateTransitionChoice> ChoicesByEvent { get; } =
            new(StringComparer.Ordinal);
        public HashSet<string> EventDefinitionIds { get; } = new(StringComparer.Ordinal);

        public void AddUnconditional(IndexedStateTransitionDefinition transition)
        {
            if (!UnconditionalTransitionsByEvent.TryGetValue(transition.SourcePeriodicEventDefinitionId, out var existing))
            {
                existing = Array.Empty<IndexedStateTransitionDefinition>();
            }

            UnconditionalTransitionsByEvent[transition.SourcePeriodicEventDefinitionId] = existing
                .Concat(new[] { transition })
                .OrderBy(item => item.IndexedStateTransitionDefinitionId, StringComparer.Ordinal)
                .ToArray();
        }
    }
}

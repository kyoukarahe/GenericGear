using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class CyclicIndexedStateTransitionEngine
{
    public HybridStateTransitionResult Apply(
        HybridStateSnapshot initialSnapshot,
        IndexedStateTransitionPlan transitionPlan,
        ContinuousEventBridgeResult eventResult,
        HybridStateAdvanceRequest request,
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
        var requestId = HybridStateTransitionIdentity.ComputeAdvanceRequestId(
            initialSnapshot,
            transitionPlan,
            eventResult.EventBridgeRequestId,
            request);

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                HybridStateTransitionContract.DefaultDeterminismProfile))
        {
            unsupported = true;
            diagnostics.Add(Error(
                DiagnosticCodes.StateUnsupportedProfile,
                "Unsupported indexed-state determinism profile '" + request.DeterminismProfile + "'."));
        }

        if (request.MaxAppliedOccurrences <= 0)
        {
            invalid = true;
            diagnostics.Add(Error(
                DiagnosticCodes.StateInvalidBudget,
                "maxAppliedOccurrences must be greater than zero."));
        }

        var definitions = ValidatePlan(transitionPlan, diagnostics, ref invalid);
        var transitionsByEvent = transitionPlan.OrderedTransitionDefinitions
            .GroupBy(item => item.SourcePeriodicEventDefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.IndexedStateTransitionDefinitionId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        ValidateSnapshot(initialSnapshot, definitions, transitionsByEvent.Keys, diagnostics, ref invalid);
        ValidateEventEnvelope(initialSnapshot, eventResult, transitionsByEvent.Keys, diagnostics, ref invalid);
        validationWatch.Stop();

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(Error(DiagnosticCodes.StateCancelled, "Indexed-state transition was cancelled before occurrence application."));
            totalWatch.Stop();
            return Terminal(
                eventResult,
                transitionPlan,
                request,
                requestId,
                HybridStateTransitionStatus.Cancelled,
                initialSnapshot,
                diagnostics,
                new HybridStateTransitionPerformance(
                    ToMicroseconds(validationWatch.ElapsedTicks), 0, 0, 0, ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        var occurrenceWatch = Stopwatch.StartNew();
        var occurrences = eventResult.Occurrences
            .OrderBy(item => item.RootTurnsAtCrossing)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.OccurrenceId, StringComparer.Ordinal)
            .ToList();
        ValidateOccurrences(initialSnapshot, eventResult, transitionsByEvent, occurrences, diagnostics, ref invalid);
        occurrenceWatch.Stop();

        if (invalid || unsupported)
        {
            totalWatch.Stop();
            return Terminal(
                eventResult,
                transitionPlan,
                request,
                requestId,
                invalid ? HybridStateTransitionStatus.InvalidInput : HybridStateTransitionStatus.Unsupported,
                initialSnapshot,
                diagnostics,
                new HybridStateTransitionPerformance(
                    ToMicroseconds(validationWatch.ElapsedTicks),
                    ToMicroseconds(occurrenceWatch.ElapsedTicks),
                    0,
                    0,
                    ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        var applicationWatch = Stopwatch.StartNew();
        var stateValues = initialSnapshot.States.ToDictionary(
            item => item.StateDefinitionId,
            item => item.CurrentIndex,
            StringComparer.Ordinal);
        var cursors = initialSnapshot.EventCursors.ToDictionary(
            item => item.PeriodicEventDefinitionId,
            item => item.LastAppliedOrdinal,
            StringComparer.Ordinal);
        var applications = new List<StateTransitionApplication>();
        var appliedOccurrenceCount = 0;
        Rational checkpointRoot = initialSnapshot.ExactRootTurns;
        var cancelled = false;
        var maximum = Math.Min(request.MaxAppliedOccurrences, occurrences.Count);
        for (var occurrenceIndex = 0; occurrenceIndex < maximum; occurrenceIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                diagnostics.Add(Error(DiagnosticCodes.StateCancelled, "Indexed-state transition observed cancellation between occurrence groups."));
                break;
            }

            var occurrence = occurrences[occurrenceIndex];
            var pendingValues = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
            var pendingApplications = new List<StateTransitionApplication>();
            foreach (var transition in transitionsByEvent[occurrence.PeriodicEventDefinitionId])
            {
                var definition = definitions[transition.TargetCyclicStateDefinitionId];
                var before = stateValues[transition.TargetCyclicStateDefinitionId];
                var after = EuclideanModulo(before + transition.SignedDelta, definition.CycleLength);
                pendingValues.Add(transition.TargetCyclicStateDefinitionId, after);
                pendingApplications.Add(new StateTransitionApplication(
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

            foreach (var update in pendingValues)
            {
                stateValues[update.Key] = update.Value;
            }

            applications.AddRange(pendingApplications);
            cursors[occurrence.PeriodicEventDefinitionId] = occurrence.EventOrdinal;
            appliedOccurrenceCount++;
            checkpointRoot = occurrence.RootTurnsAtCrossing;
        }

        applicationWatch.Stop();
        var snapshotWatch = Stopwatch.StartNew();
        var known = occurrences.Count;
        var omitted = known - appliedOccurrenceCount;
        var status = cancelled
            ? HybridStateTransitionStatus.Cancelled
            : omitted > 0
                ? HybridStateTransitionStatus.IncompleteBudget
                : HybridStateTransitionStatus.Complete;
        var snapshotRoot = status == HybridStateTransitionStatus.Complete
            ? eventResult.NormalizedRequest.CurrentRootTurns
            : checkpointRoot;
        var materializedSnapshot = CreateSnapshot(initialSnapshot, snapshotRoot, stateValues, cursors);
        var checkpoint = status == HybridStateTransitionStatus.Complete ? null : materializedSnapshot;
        var finalSnapshot = status == HybridStateTransitionStatus.Complete ? materializedSnapshot : null;
        if (status == HybridStateTransitionStatus.IncompleteBudget)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.StateResultTruncated,
                DiagnosticSeverity.Warning,
                "The canonical occurrence sequence exceeded maxAppliedOccurrences."));
        }

        snapshotWatch.Stop();
        totalWatch.Stop();
        return new HybridStateTransitionResult(
            eventResult,
            transitionPlan,
            request,
            requestId,
            status,
            status == HybridStateTransitionStatus.Complete,
            status == HybridStateTransitionStatus.IncompleteBudget,
            new HybridStateTransitionSummary(known, appliedOccurrenceCount, omitted, applications.Count),
            initialSnapshot,
            applications,
            checkpoint,
            finalSnapshot,
            diagnostics,
            new HybridStateTransitionPerformance(
                ToMicroseconds(validationWatch.ElapsedTicks),
                ToMicroseconds(occurrenceWatch.ElapsedTicks),
                ToMicroseconds(applicationWatch.ElapsedTicks),
                ToMicroseconds(snapshotWatch.ElapsedTicks),
                ToMicroseconds(totalWatch.ElapsedTicks)));
    }

    private static Dictionary<string, CyclicIndexedStateDefinition> ValidatePlan(
        IndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        var definitions = new Dictionary<string, CyclicIndexedStateDefinition>(StringComparer.Ordinal);
        var definitionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in plan.OrderedStateDefinitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Key) || definition.CycleLength <= BigInteger.Zero)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateInvalidDefinition,
                    "A cyclic state requires a stable key and a strictly positive cycle length.",
                    definition.CyclicStateDefinitionId));
            }

            if (!definitionKeys.Add(definition.Key) ||
                !definitions.TryAdd(definition.CyclicStateDefinitionId, definition))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateDuplicateDefinition,
                    "Cyclic state keys and definition IDs must be unique.",
                    definition.CyclicStateDefinitionId));
            }
        }

        if (definitions.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateInvalidDefinition, "At least one cyclic state definition is required."));
        }

        var transitionIds = new HashSet<string>(StringComparer.Ordinal);
        var transitionKeys = new HashSet<string>(StringComparer.Ordinal);
        var sourceTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transition in plan.OrderedTransitionDefinitions)
        {
            if (string.IsNullOrWhiteSpace(transition.Key) ||
                string.IsNullOrWhiteSpace(transition.SourcePeriodicEventDefinitionId) ||
                string.IsNullOrWhiteSpace(transition.TargetCyclicStateDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateInvalidTransition,
                    "An indexed transition requires a key, source event definition and target state.",
                    transition.IndexedStateTransitionDefinitionId));
            }

            if (!transitionIds.Add(transition.IndexedStateTransitionDefinitionId) ||
                !transitionKeys.Add(transition.Key))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateDuplicateTransition,
                    "Indexed transition keys and definition IDs must be unique.",
                    transition.IndexedStateTransitionDefinitionId));
            }

            if (!definitions.ContainsKey(transition.TargetCyclicStateDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateUnknownTarget,
                    "The indexed transition references an unknown target state.",
                    transition.TargetCyclicStateDefinitionId));
            }

            var sourceTarget = transition.SourcePeriodicEventDefinitionId + "\u001f" +
                transition.TargetCyclicStateDefinitionId;
            if (!sourceTargets.Add(sourceTarget))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.StateAmbiguousTransition,
                    "Two transitions from the same event cannot update the same state in v0.1.",
                    transition.TargetCyclicStateDefinitionId));
            }
        }

        if (plan.OrderedTransitionDefinitions.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateInvalidTransition, "At least one indexed transition is required."));
        }

        return definitions;
    }

    private static void ValidateSnapshot(
        HybridStateSnapshot snapshot,
        IReadOnlyDictionary<string, CyclicIndexedStateDefinition> definitions,
        IEnumerable<string> eventDefinitionIds,
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

            if (!definitions.TryGetValue(state.StateDefinitionId, out var definition))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateMissingSnapshotValue, "Snapshot contains a state outside the transition plan.", state.StateDefinitionId));
            }
            else if (state.CurrentIndex < BigInteger.Zero || state.CurrentIndex >= definition.CycleLength)
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateInvalidSnapshot, "Snapshot state index is outside its canonical cycle range.", state.StateDefinitionId));
            }
        }

        foreach (var definitionId in definitions.Keys.Where(item => !states.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingSnapshotValue, "Snapshot is missing a transition-plan state.", definitionId));
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

        var expectedEvents = new HashSet<string>(eventDefinitionIds, StringComparer.Ordinal);
        foreach (var unexpected in cursors.Where(item => !expectedEvents.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor, "Snapshot contains a cursor outside the transition plan.", unexpected));
        }

        foreach (var missing in expectedEvents.Where(item => !cursors.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateMissingCursor, "Snapshot is missing a transition-plan event cursor.", missing));
        }
    }

    private static void ValidateEventEnvelope(
        HybridStateSnapshot snapshot,
        ContinuousEventBridgeResult eventResult,
        IEnumerable<string> transitionEventDefinitionIds,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        if (!eventResult.IsSuccess ||
            eventResult.Status != ContinuousEventBridgeStatus.Complete ||
            !eventResult.SearchComplete || eventResult.ResultTruncated ||
            eventResult.SearchSummary.OmittedOccurrenceCount != BigInteger.Zero ||
            eventResult.SearchSummary.KnownTotalOccurrenceCount != eventResult.Occurrences.Count ||
            eventResult.SearchSummary.EmittedOccurrenceCount != eventResult.Occurrences.Count)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateEventStreamIncomplete, "Only a complete, non-truncated exact event stream can advance authoritative state."));
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
        var actual = new HashSet<string>(
            eventResult.NormalizedDefinitions.Select(item => item.PeriodicEventDefinitionId),
            StringComparer.Ordinal);
        if (actual.Count != eventResult.NormalizedDefinitions.Count)
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Event stream contains duplicate normalized definition IDs."));
        }
        foreach (var unsupportedEvent in actual.Where(item => !expected.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Event stream contains a definition outside the transition plan.", unsupportedEvent));
        }

        foreach (var missingEvent in expected.Where(item => !actual.Contains(item)))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Event stream omits a transition-plan event definition.", missingEvent));
        }
    }

    private static void ValidateOccurrences(
        HybridStateSnapshot snapshot,
        ContinuousEventBridgeResult eventResult,
        IReadOnlyDictionary<string, IndexedStateTransitionDefinition[]> transitionsByEvent,
        IReadOnlyList<PeriodicEventOccurrence> occurrences,
        ICollection<Diagnostic> diagnostics,
        ref bool invalid)
    {
        var occurrenceIds = new HashSet<string>(StringComparer.Ordinal);
        var definitionOrdinals = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        foreach (var cursor in snapshot.EventCursors)
        {
            if (!cursors.ContainsKey(cursor.PeriodicEventDefinitionId))
            {
                cursors.Add(cursor.PeriodicEventDefinitionId, cursor.LastAppliedOrdinal);
            }
        }
        foreach (var occurrence in occurrences)
        {
            var signature = occurrence.PeriodicEventDefinitionId + "\u001f" + occurrence.EventOrdinal;
            if (!occurrenceIds.Add(occurrence.OccurrenceId) || !definitionOrdinals.Add(signature))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateOccurrenceDuplicate, "The event stream contains a duplicate occurrence.", occurrence.OccurrenceId));
            }

            if (!transitionsByEvent.ContainsKey(occurrence.PeriodicEventDefinitionId))
            {
                invalid = true;
                diagnostics.Add(Error(DiagnosticCodes.StateUnknownEventDefinition, "Occurrence has no matching indexed transition.", occurrence.PeriodicEventDefinitionId));
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

    private static HybridStateTransitionResult Terminal(
        ContinuousEventBridgeResult eventResult,
        IndexedStateTransitionPlan plan,
        HybridStateAdvanceRequest request,
        string requestId,
        HybridStateTransitionStatus status,
        HybridStateSnapshot initialSnapshot,
        IEnumerable<Diagnostic> diagnostics,
        HybridStateTransitionPerformance performance) =>
        new(
            eventResult,
            plan,
            request,
            requestId,
            status,
            false,
            false,
            new HybridStateTransitionSummary(
                eventResult.Occurrences.Count,
                BigInteger.Zero,
                eventResult.Occurrences.Count,
                BigInteger.Zero),
            initialSnapshot,
            Array.Empty<StateTransitionApplication>(),
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
}

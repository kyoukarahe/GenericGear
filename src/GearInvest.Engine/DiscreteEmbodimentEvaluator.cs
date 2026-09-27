using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;
using static GearInvest.Engine.DiscreteEmbodimentCompiler;

namespace GearInvest.Engine;

/// <summary>Ideal positive indexing. No semantic reducer, calendar data, after-index, or logical branch is an input.</summary>
public sealed class DiscreteEmbodimentEvaluator
{
    public DiscreteActuationResult Evaluate(DiscreteEmbodimentModel model, DiscreteEmbodimentState initial,
        ContinuousEventBridgeResult events, int maxOccurrences, CancellationToken cancellationToken = default)
    {
        var cycles = new List<DiscreteActuationCycle>();
        try
        {
            Need(new DiscreteEmbodimentCompiler().Validate(model).IsValid, "invalid-mechanical-model");
            ValidateState(model, initial);
            Need(maxOccurrences >= 0 && maxOccurrences <= 4096, "execution-budget-bound");
            ValidateEvents(model, initial, events);
            var current = initial;
            foreach (var occurrence in events.Occurrences.Take(maxOccurrences))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cycle = ExecuteCycle(model, current, occurrence.OccurrenceId, occurrence.EventOrdinal, occurrence.RootTurnsAtCrossing);
                cycles.Add(cycle); current = cycle.After;
            }
            bool complete = cycles.Count == events.Occurrences.Count;
            var final = complete ? AtRoot(current, events.NormalizedRequest.CurrentRootTurns) : null;
            return Result(complete ? DiscreteStatus.Complete : DiscreteStatus.IncompleteBudget, final, current, Array.Empty<string>());
        }
        catch (OperationCanceledException) { return Result(DiscreteStatus.Cancelled, null, cycles.LastOrDefault()?.After, new[] { "cancelled" }); }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
        { return Result(DiscreteStatus.InvalidInput, null, null, new[] { ex.Message }); }
        DiscreteActuationResult Result(DiscreteStatus status, DiscreteEmbodimentState? final, DiscreteEmbodimentState? checkpoint, IEnumerable<string> errors) =>
            new DiscreteActuationResult(model.ModelId, model.BindingId, events.EventBridgeRequestId, maxOccurrences, status, events.Occurrences.Count,
                events.NormalizedRequest.CurrentRootTurns, initial, cycles, final, checkpoint, Certificate(errors, "indexed-motion"));
    }

    public static void ValidateState(DiscreteEmbodimentModel model, DiscreteEmbodimentState state)
    {
        ValidatePose(model, state);
        var sensed = Sense(model, state);
        Need(sensed.position <= sensed.limit, "inadmissible-indexed-pair-state");
    }

    internal static void ValidatePose(DiscreteEmbodimentModel model, DiscreteEmbodimentState state)
    {
        Need(state.ModelId == model.ModelId && !string.IsNullOrWhiteSpace(state.CandidateId) && !string.IsNullOrWhiteSpace(state.DriverId), "state-source-identity");
        Need(state.Positions.Count == 2 && state.Positions.Select(p => p.WheelId).SequenceEqual(new[] { "primary", "secondary" }), "wheel-state-coverage");
        foreach (var p in state.Positions)
        {
            int n = model.Components.Single(c => c.Id == p.WheelId).Detents;
            Need(p.Index >= 0 && p.Index < n && Fraction(p.UnwrappedTurns) == new Rational(p.Index, n), "invalid-or-unsettled-wheel-state");
        }
    }

    internal static void ValidateEvents(DiscreteEmbodimentModel model, DiscreteEmbodimentState initial, ContinuousEventBridgeResult e)
    {
        Need(e.IsSuccess && e.SearchComplete && !e.ResultTruncated && e.Occurrences.Count <= 4096 &&
            e.SearchSummary.DefinitionCount == 1 && e.SearchSummary.KnownTotalOccurrenceCount == e.Occurrences.Count &&
            e.SearchSummary.EmittedOccurrenceCount == e.Occurrences.Count && e.SearchSummary.OmittedOccurrenceCount == 0, "incomplete-event-envelope");
        Need(e.Source.CandidateId == initial.CandidateId && e.Source.DriverId == initial.DriverId && e.NormalizedRequest.DriverId == initial.DriverId &&
            e.NormalizedRequest.PreviousRootTurns == initial.RootTurns && e.NormalizedRequest.CurrentRootTurns >= initial.RootTurns, "event-source-or-window-mismatch");
        Need(e.NormalizedDefinitions.Count == 1 && e.NormalizedDefinitions[0].PeriodicEventDefinitionId == model.Projection.EventDefinitionId, "event-binding-mismatch");
        var d = e.NormalizedDefinitions[0];
        Need(d.ExactSourceCoefficient > Rational.Zero && d.PeriodTurns > Rational.Zero && d.NormalizedCrossingPhaseTurns >= Rational.Zero &&
            d.NormalizedCrossingPhaseTurns < d.PeriodTurns && d.TraversalPolicy == PeriodicEventTraversalPolicy.ForwardOnly, "unsupported-event-direction");
        Need(d.PeriodicEventDefinitionId == ContinuousEventBridgeIdentity.ComputeDefinitionId(initial.CandidateId, initial.DriverId, d.SourceDofId,
            d.PeriodTurns, d.NormalizedCrossingPhaseTurns, d.TraversalPolicy, e.BackendFingerprint), "event-definition-identity");
        var canonical = ContinuousEventBridgeIdentity.BuildRequestCanonicalRepresentation(initial.CandidateId, e.NormalizedRequest, e.NormalizedDefinitions);
        Need(canonical == e.RequestCanonicalRepresentation && e.EventBridgeRequestId == ContinuousEventBridgeIdentity.ComputeRequestId(canonical), "event-request-identity");
        var first = Floor((d.ExactSourceCoefficient * initial.RootTurns + d.ExactSourcePhase - d.NormalizedCrossingPhaseTurns) / d.PeriodTurns) + 1;
        var last = Floor((d.ExactSourceCoefficient * e.NormalizedRequest.CurrentRootTurns + d.ExactSourcePhase - d.NormalizedCrossingPhaseTurns) / d.PeriodTurns);
        Need(initial.Cursor==first-1,"initial-cursor-does-not-match-event-window");
        Need(last - first + 1 == e.Occurrences.Count, "missing-event-occurrences");
        BigInteger cursor = initial.Cursor;
        Rational previous = initial.RootTurns;
        foreach (var o in e.Occurrences)
        {
            Need(o.EventOrdinal == cursor + 1 && o.EventOrdinal == first++ && o.PeriodicEventDefinitionId == d.PeriodicEventDefinitionId &&
                o.DriverId == initial.DriverId && o.SourceDofId == d.SourceDofId && o.TraversalDirection == PeriodicEventTraversalDirection.Forward, "duplicate-stale-gap-or-foreign-event");
            Need(o.OccurrenceId == ContinuousEventBridgeIdentity.ComputeOccurrenceId(d.PeriodicEventDefinitionId, o.EventOrdinal, o.TraversalDirection), "occurrence-identity");
            Need(o.RootTurnsAtCrossing > previous && o.RootTurnsAtCrossing <= e.NormalizedRequest.CurrentRootTurns &&
                o.SourceTurnsAtCrossing == new Rational(o.EventOrdinal) * d.PeriodTurns + d.NormalizedCrossingPhaseTurns &&
                o.SourceTurnsAtCrossing == d.ExactSourceCoefficient * o.RootTurnsAtCrossing + d.ExactSourcePhase, "occurrence-crossing-equation");
            previous = o.RootTurnsAtCrossing; cursor = o.EventOrdinal;
        }
    }

    private static (Rational position, Rational limit) Sense(DiscreteEmbodimentModel m, DiscreteEmbodimentState state)
    {
        var components = m.Components.ToDictionary(x => x.Id, StringComparer.Ordinal);
        Rational Read(string track, string probe, string wheel)
        {
            int index = state.Positions.Single(x => x.WheelId == wheel).Index;
            var follower = components[probe];
            return new Rational(components[track].Sectors[index]) * follower.Scale + follower.Offset;
        }
        return (Read("position-track", "position-probe", "primary"), Read("limit-track", "limit-probe", "secondary"));
    }

    /// <summary>Low-level replay verifier uses this same primitive interpreter, never a logical reference result.</summary>
    internal static DiscreteActuationCycle ExecuteCycle(DiscreteEmbodimentModel m, DiscreteEmbodimentState before, string occurrenceId, BigInteger ordinal, Rational eventRoot)
        => ExecuteIndexedCycle(m, before, occurrenceId, ordinal, eventRoot, state => Sense(m, state));

    // A bounded internal seam, not a public plugin mechanism. The 14B caller supplies geometry readback only.
    internal static DiscreteActuationCycle ExecuteIndexedCycle(DiscreteEmbodimentModel m, DiscreteEmbodimentState before,
        string occurrenceId, BigInteger ordinal, Rational eventRoot, Func<DiscreteEmbodimentState, (Rational position, Rational limit)> sense)
    {
        ValidatePose(m, before);
        Need(ordinal == before.Cursor + 1 && eventRoot > before.RootTurns, "cycle-cursor-or-time");
        var c = m.Components.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var p = before.Positions.ToDictionary(x => x.WheelId, StringComparer.Ordinal);
        var readings = sense(before);
        Need(readings.position <= readings.limit, "inadmissible-indexed-pair-state");
        // The differential selector is sensed once. The latch is not reevaluated during transient reset detents.
        var route = readings.position < readings.limit ? DiscreteRoute.SingleStep : DiscreteRoute.SeekReference;
        var b = m.Motion.Boundaries;
        var actions = new List<DiscreteMicroAction>();
        void Mark(DiscreteActionKind k, string component, Rational start, Rational end) => actions.Add(new DiscreteMicroAction(k, component, "", start, end, Rational.Zero, Rational.Zero));
        Mark(DiscreteActionKind.Sense, "selector", b[0], b[1]); Mark(DiscreteActionKind.Latch, "latch", b[1], b[2]);
        int capacity = c["primary-indexer"].Capacity, used = 0;
        var slot = (b[3] - b[2]) / new Rational(capacity);
        void Pulse(string wheel, Rational start, Rational driveEnd, Rational returnEnd, Rational settleEnd)
        {
            var from = p[wheel]; int n = c[wheel].Detents;
            var to = from.UnwrappedTurns + new Rational(1, n);
            actions.Add(new DiscreteMicroAction(DiscreteActionKind.Drive, wheel + "-indexer", wheel, start, driveEnd, from.UnwrappedTurns, to));
            actions.Add(new DiscreteMicroAction(DiscreteActionKind.Return, wheel + "-indexer", wheel, driveEnd, returnEnd, to, to));
            actions.Add(new DiscreteMicroAction(DiscreteActionKind.Settle, wheel + "-lock", wheel, returnEnd, settleEnd, to, to));
            p[wheel] = new DiscreteWheelPosition(wheel, (from.Index + 1) % n, to);
        }
        // Positive pulses physically visit every intervening detent. Home stop inhibits subsequent strokes.
        while (route == DiscreteRoute.SingleStep ? used < 1 : p["primary"].Index != c["stop"].Reference)
        {
            Need(used < capacity, "reference-not-reached-within-pulse-capacity");
            var start = b[2] + new Rational(used) * slot;
            Pulse("primary", start, start + slot / new Rational(3), start + slot * new Rational(2, 3), start + slot);
            used++;
        }
        if (used < capacity) Mark(DiscreteActionKind.Hold, "primary-lock", b[2] + new Rational(used) * slot, b[3]);
        Mark(route == DiscreteRoute.SeekReference ? DiscreteActionKind.ReferenceConfirmed : DiscreteActionKind.Hold, "stop", b[3], b[4]);
        if (route == DiscreteRoute.SeekReference)
        {
            Need(p["primary"].Index == c["stop"].Reference, "secondary-interlock-not-home");
            Need(c["secondary-indexer"].Capacity >= 1, "secondary-drive-capacity");
            Pulse("secondary", b[4], b[5], b[6], b[7]);
        }
        else Mark(DiscreteActionKind.Hold, "secondary-lock", b[4], b[7]);
        Mark(DiscreteActionKind.Hold, "sequence", b[7], b[8]);
        Mark(DiscreteActionKind.Commit, "sequence", b[8], b[8]);
        var after = new DiscreteEmbodimentState(m.ModelId, before.CandidateId, before.DriverId, eventRoot, ordinal, p.Values);
        ValidatePose(m, after);
        var settled = sense(after);
        Need(settled.position <= settled.limit, "inadmissible-indexed-pair-state");
        return new DiscreteActuationCycle(m.ModelId, occurrenceId, ordinal, eventRoot, before, after, readings.position, readings.limit, route, actions);
    }

    public DiscreteValidation ValidateResult(DiscreteEmbodimentModel model, DiscreteActuationResult result)
    {
        try
        {
            Need(new DiscreteEmbodimentCompiler().Validate(model).IsValid, "invalid-mechanical-model");
            Need(result.ModelId == model.ModelId && result.BindingId == model.BindingId, "result-model-binding");
            ValidateState(model, result.Initial);
            Need(result.Budget >= 0 && result.Budget <= 4096 && result.KnownOccurrences >= 0 && result.KnownOccurrences <= 4096 && result.Cycles.Count <= result.Budget &&
                result.Cycles.Count <= result.KnownOccurrences && result.ToRoot >= result.Initial.RootTurns, "result-bounds");
            Need(result.Status == DiscreteStatus.Complete || result.Status == DiscreteStatus.IncompleteBudget, "non-replayable-result-status");
            var current = result.Initial;
            foreach (var cycle in result.Cycles)
            {
                Need(cycle.Before.StateId == current.StateId && cycle.EventRoot <= result.ToRoot, "broken-cycle-chain");
                var expectedId = ContinuousEventBridgeIdentity.ComputeOccurrenceId(model.Projection.EventDefinitionId, cycle.Ordinal, PeriodicEventTraversalDirection.Forward);
                Need(cycle.OccurrenceId == expectedId, "cycle-occurrence-identity");
                var actual = ExecuteCycle(model, current, cycle.OccurrenceId, cycle.Ordinal, cycle.EventRoot);
                Need(actual.CycleId == cycle.CycleId && actual.After.StateId == cycle.After.StateId, "trace-not-produced-by-mechanical-model");
                current = actual.After;
            }
            Need(result.Checkpoint?.StateId == current.StateId, "invalid-checkpoint");
            if (result.Status == DiscreteStatus.Complete)
                Need(result.Cycles.Count == result.KnownOccurrences && result.Final?.StateId == AtRoot(current, result.ToRoot).StateId, "invalid-complete-result");
            else Need(result.Final == null && result.Cycles.Count == result.Budget && result.KnownOccurrences > result.Budget, "invalid-budget-result");
            Need(result.Validation.IsValid, "stored-validation-failed");
            var certificate = Certificate(Array.Empty<string>(), "indexed-motion");
            Need(result.Validation.Levels.Select(l => DiscreteEmbodimentContract.Pack(new[] { l.Level, l.Status, l.Detail })).SequenceEqual(
                certificate.Levels.Select(l => DiscreteEmbodimentContract.Pack(new[] { l.Level, l.Status, l.Detail }))), "unverified-validation-level-claim");
            return Certificate(Array.Empty<string>(), "indexed-motion");
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { return Certificate(new[] { ex.Message }, "indexed-motion"); }
    }

    /// <summary>Sampling has no clock, mutation, event consumption, or frame-rate dependence.</summary>
    public DiscreteActuationFrame Sample(DiscreteEmbodimentModel model, DiscreteActuationCycle cycle, Rational phase)
    {
        Need(phase >= Rational.Zero && phase <= Rational.One, "phase-out-of-range");
        Need(new DiscreteEmbodimentCompiler().Validate(model).IsValid && cycle.ModelId == model.ModelId, "invalid-frame-model");
        Need(ExecuteCycle(model, cycle.Before, cycle.OccurrenceId, cycle.Ordinal, cycle.EventRoot).CycleId == cycle.CycleId, "invalid-frame-trace");
        return SampleResolvedCycle(model, cycle, phase);
    }

    internal static DiscreteActuationFrame SampleResolvedCycle(DiscreteEmbodimentModel model, DiscreteActuationCycle cycle, Rational phase)
    {
        Need(phase >= Rational.Zero && phase <= Rational.One, "phase-out-of-range");
        var poses = cycle.Before.Positions.ToDictionary(p => p.WheelId, StringComparer.Ordinal);
        var active = cycle.Actions.Last();
        Rational stroke = Rational.Zero;
        foreach (var a in cycle.Actions)
        {
            if (a.WheelId.Length > 0 && a.End <= phase)
                poses[a.WheelId] = Pose(model, a.WheelId, a.ToTurns);
            if (a.Begin <= phase && phase < a.End)
            {
                active = a;
                var t = (phase - a.Begin) / (a.End - a.Begin);
                if (a.Kind == DiscreteActionKind.Drive)
                { stroke = t; poses[a.WheelId] = Pose(model, a.WheelId, a.FromTurns + (a.ToTurns - a.FromTurns) * t); }
                else if (a.Kind == DiscreteActionKind.Return) stroke = Rational.One - t;
                break;
            }
        }
        // The positive holding detent engages at drive end, holds during return, and is confirmed at settle end.
        var unlocked = active.Kind == DiscreteActionKind.Drive ? new[] { active.WheelId } : Array.Empty<string>();
        bool committed = phase == Rational.One;
        return new DiscreteActuationFrame(cycle.CycleId, phase, active.Kind, cycle.Route, phase >= model.Motion.Boundaries[2], committed,
            active.ComponentId, stroke, poses.Values.OrderBy(p => p.WheelId, StringComparer.Ordinal), unlocked, committed ? cycle.After : cycle.Before,
            cycle.Ordinal, cycle.EventRoot, cycle.PositionReading, cycle.LimitReading);
    }
    private static DiscreteWheelPosition Pose(DiscreteEmbodimentModel model, string id, Rational turns) =>
        new DiscreteWheelPosition(id, (int)Floor(Fraction(turns) * new Rational(model.Components.Single(c => c.Id == id).Detents)), turns);
    internal static BigInteger Floor(Rational r) { var q = BigInteger.DivRem(r.Numerator, r.Denominator, out var rem); return rem.Sign < 0 ? q - 1 : q; }
    private static Rational Fraction(Rational r) => r - new Rational(Floor(r));
    internal static DiscreteEmbodimentState AtRoot(DiscreteEmbodimentState s, Rational root) => new DiscreteEmbodimentState(s.ModelId, s.CandidateId, s.DriverId, root, s.Cursor, s.Positions);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.DiscreteEmbodimentCompiler;

namespace GearInvest.Engine;

/// <summary>Surface readback drives the shared positive-indexing interpreter. The idealized 14A evaluator and logical reducer are NOT called.</summary>
public sealed class GeometricDiscreteEvaluator
{
    public GeometricDiscreteResult Evaluate(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry,
        DiscreteEmbodimentState initial, ContinuousEventBridgeResult events, int maxOccurrences, CancellationToken cancellationToken = default)
    {
        var cycles = new List<GeometricDiscreteCycle>();
        var validation = new DiscreteGeometryGenerator().Validate(model, geometry);
        try
        {
            Need(validation.IsValid, "geometry-not-approved-for-execution");
            DiscreteEmbodimentEvaluator.ValidatePose(model, initial);
            var reading = Sense(geometry, initial); Need(reading.position <= reading.limit, "inadmissible-geometric-state");
            Need(maxOccurrences >= 0 && maxOccurrences <= 4096, "execution-budget-bound");
            DiscreteEmbodimentEvaluator.ValidateEvents(model, initial, events);
            var current = initial;
            foreach (var occurrence in events.Occurrences.Take(maxOccurrences))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cycle = Execute(model, geometry, current, occurrence.OccurrenceId, occurrence.EventOrdinal, occurrence.RootTurnsAtCrossing);
                cycles.Add(cycle); current = cycle.Motion.After;
            }
            bool complete = cycles.Count == events.Occurrences.Count;
            return Result(complete ? DiscreteStatus.Complete : DiscreteStatus.IncompleteBudget,
                complete ? DiscreteEmbodimentEvaluator.AtRoot(current, events.NormalizedRequest.CurrentRootTurns) : null, current, ExecutionCertificate(validation));
        }
        catch (OperationCanceledException) { return Result(DiscreteStatus.Cancelled, null, cycles.LastOrDefault()?.Motion.After, Failure(validation, "cancelled")); }
        catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is OverflowException)
        { return Result(DiscreteStatus.InvalidInput, null, null, Failure(validation, e.Message)); }
        GeometricDiscreteResult Result(DiscreteStatus status, DiscreteEmbodimentState? final, DiscreteEmbodimentState? checkpoint, DiscreteGeometryValidation certificate) =>
            new GeometricDiscreteResult(geometry.GeometryId, geometry.RealizationBindingId, model.ModelId, events.EventBridgeRequestId, initial,
                events.NormalizedRequest.CurrentRootTurns, maxOccurrences, events.Occurrences.Count, status, cycles, final, checkpoint, certificate);
    }

    private static DiscreteGeometryHit[] Hits(DiscreteGeometryCandidate geometry, DiscreteEmbodimentState state) => geometry.Stations.Select(s =>
        AxialTrackQuery.Read(geometry, s.ProbeId, state.Positions.Single(p => p.WheelId == s.WheelId).UnwrappedTurns)).ToArray();
    private static (Rational position, Rational limit) Sense(DiscreteGeometryCandidate geometry, DiscreteEmbodimentState state)
    {
        var hits = Hits(geometry, state); Need(hits.All(h => h.Status == GeometryHitStatus.Hit), "geometric-sensing-failed");
        return (hits.Single(h => h.WheelId == "primary").Value, hits.Single(h => h.WheelId == "secondary").Value);
    }
    private static GeometricDiscreteCycle Execute(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, DiscreteEmbodimentState before,
        string occurrenceId, System.Numerics.BigInteger ordinal, Rational eventRoot)
    {
        var hits = Hits(geometry, before);
        Need(hits.All(h => h.Status == GeometryHitStatus.Hit), "geometric-sensing-failed");
        var motion = DiscreteEmbodimentEvaluator.ExecuteIndexedCycle(model, before, occurrenceId, ordinal, eventRoot, state => Sense(geometry, state));
        return new GeometricDiscreteCycle(geometry.GeometryId, motion, hits);
    }

    public DiscreteGeometryValidation ValidateResult(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, GeometricDiscreteResult result)
    {
        var validation = new DiscreteGeometryGenerator().Validate(model, geometry);
        try
        {
            Need(validation.IsValid, "invalid-geometry");
            Need(result.ModelId == model.ModelId && result.GeometryId == geometry.GeometryId && result.RealizationBindingId == geometry.RealizationBindingId, "result-realization-binding");
            DiscreteEmbodimentEvaluator.ValidatePose(model, result.Initial);
            var initial = Sense(geometry, result.Initial); Need(initial.position <= initial.limit, "inadmissible-initial-state");
            Need(result.Budget >= 0 && result.Budget <= 4096 && result.Known >= 0 && result.Known <= 4096 && result.Applied <= result.Budget && result.Applied <= result.Known && result.ToRoot >= result.Initial.RootTurns, "result-bounds");
            Need(result.Status == DiscreteStatus.Complete || result.Status == DiscreteStatus.IncompleteBudget, "non-replayable-result-status");
            var current = result.Initial;
            foreach (var cycle in result.Cycles)
            {
                var c = cycle.Motion;
                Need(c.Before.StateId == current.StateId && c.EventRoot <= result.ToRoot, "broken-cycle-chain");
                Need(c.OccurrenceId == ContinuousEventBridgeIdentity.ComputeOccurrenceId(model.Projection.EventDefinitionId, c.Ordinal, PeriodicEventTraversalDirection.Forward), "cycle-occurrence-identity");
                var actual = Execute(model, geometry, current, c.OccurrenceId, c.Ordinal, c.EventRoot);
                Need(actual.CycleId == cycle.CycleId && actual.Motion.After.StateId == c.After.StateId, "geometric-witness-or-motion-mismatch");
                current = actual.Motion.After;
            }
            Need(result.Checkpoint?.StateId == current.StateId, "invalid-checkpoint");
            if (result.Status == DiscreteStatus.Complete)
                Need(result.Applied == result.Known && result.Final?.StateId == DiscreteEmbodimentEvaluator.AtRoot(current, result.ToRoot).StateId, "invalid-complete-result");
            else Need(result.Final == null && result.Applied == result.Budget && result.Known > result.Budget, "invalid-budget-result");
            var certificate = ExecutionCertificate(validation);
            Need(result.Validation.Canonical == certificate.Canonical, "unverified-validation-claim");
            return certificate;
        }
        catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is OverflowException) { return Failure(validation, e.Message); }
    }

    /// <summary>Pure replay of a resolved geometric cycle. Does not advance any cursor or consume events.</summary>
    public GeometricDiscreteFrame Sample(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, GeometricDiscreteCycle cycle, Rational phase)
    {
        Need(new DiscreteGeometryGenerator().Validate(model, geometry).IsValid, "invalid-frame-geometry");
        var c = cycle.Motion;
        Need(Execute(model, geometry, c.Before, c.OccurrenceId, c.Ordinal, c.EventRoot).CycleId == cycle.CycleId, "invalid-geometric-frame-cycle");
        var motion = DiscreteEmbodimentEvaluator.SampleResolvedCycle(model, c, phase);
        var probes = geometry.Stations.Select(s =>
        {
            var hit = cycle.Hits.Single(h => h.ProbeId == s.ProbeId);
            Rational t; string stage;
            if (phase < DiscreteGeometryContract.ApproachEnd) { t = phase / DiscreteGeometryContract.ApproachEnd; stage = "Approach"; }
            else if (phase < DiscreteGeometryContract.Latch) { t = Rational.One; stage = "Sense"; }
            else if (phase < DiscreteGeometryContract.ClearDrive) { t = (DiscreteGeometryContract.ClearDrive - phase) / (DiscreteGeometryContract.ClearDrive - DiscreteGeometryContract.Latch); stage = "Retract"; }
            else { t = Rational.Zero; stage = "Clear"; }
            return new GeometricProbePose(s.ProbeId, s.ProbeOrigin + (hit.Point - s.ProbeOrigin) * t, stage, phase >= DiscreteGeometryContract.ClearDrive);
        });
        return new GeometricDiscreteFrame(geometry.GeometryId, cycle.CycleId, motion, probes, cycle.Hits);
    }
    private static DiscreteGeometryValidation ExecutionCertificate(DiscreteGeometryValidation geometry) => new DiscreteGeometryValidation(
        geometry.Checks.Where(c => c.Domain != "indexed-motion").Concat(new[] { new GeometryCheck("indexed-motion", "geometric-readback-interpreter",
            GeometryVerdict.Pass, "exact-positive-indexing/replay", Rational.Zero, Rational.One,
            "geometry values feed selector; latched route, clear-before-drive, positive pulses, reference stop, interlock, settled commit; ideal engagement and external drive") }));
    private static DiscreteGeometryValidation Failure(DiscreteGeometryValidation geometry, string error) => new DiscreteGeometryValidation(geometry.Checks.Concat(new[] {
        new GeometryCheck("indexed-motion", "execution", GeometryVerdict.ProvenViolation, "exact-replay", Rational.Zero, Rational.One, error) }));
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class TransmissionGoalValidator
{
    public static TransmissionGoalValidation Validate(TransmissionGoal goal, TransmissionMechanismCandidate candidate, Func<GenerationCandidate, string> identity)
        => Validate(TransmissionGoalCompiler.Compile(goal), candidate, identity);

    internal static TransmissionGoalValidation Validate(CompiledTransmissionSearchPlan plan, TransmissionMechanismCandidate candidate, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>(); var contexts = new List<string>(); Rational? actual = null;
        void Check(bool value, string code) { if (!value) ds.Add(TransmissionGoalCompiler.Error(code, candidate.CandidateId, code)); }
        if (!plan.IsSupported) { ds.AddRange(plan.Diagnostics); Check(false, "UNSUPPORTED_GOAL_CONTEXT"); return new TransmissionGoalValidation(plan.GoalId, candidate.CandidateId, actual, contexts, ds); }
        var g = plan.Normalized.Goal!; var mechanism = candidate.Mechanism;
        Check(candidate.Origins.Select(o => o.OriginId).Distinct(StringComparer.Ordinal).Count() == candidate.Origins.Count, "DUPLICATE_ORIGIN");
        foreach (var origin in candidate.Origins)
        {
            var child = plan.Families.Single(p => p.Family == origin.Family);
            Check(child.Applicability == TransmissionApplicability.Eligible && child.Quota > 0, "ORIGIN_NOT_SEARCH_ELIGIBLE");
            Check(child.RequestId == origin.RequestId, "LOWERED_REQUEST_BINDING");
            Check(origin.CandidateId == candidate.CandidateId && identity(origin.Mechanism) == candidate.CandidateId, "MECHANICAL_IDENTITY");
            Check(MechanicalSignature(origin.Mechanism) == MechanicalSignature(mechanism), "SAME_ID_DIFFERENT_MECHANICAL_PAYLOAD");
            bool valid = false; string context = ""; Rational? q = null;
            if (origin.Simple != null && child.Simple != null)
            {
                var o = origin.Simple; var v = GearRouteValidator.Validate(child.Simple, o.Path, o.Mechanism, o.CandidateId);
                valid = v.IsValid; context = v.ContextId; q = v.ActualTransfer; ds.AddRange(v.Diagnostics);
            }
            else if (origin.One != null && child.One != null)
            {
                var o = origin.One; var v = CompoundGearRouteValidator.Validate(child.One, o.Pair, o.InputPath, o.OutputPath, o.Mechanism, o.CandidateId);
                valid = v.IsValid; context = v.ContextId; q = v.TotalTransfer; ds.AddRange(v.Diagnostics);
            }
            else if (origin.Two != null && child.Two != null)
            {
                var o = origin.Two; var v = TwoCompoundGearRouteValidator.Validate(child.Two, o.Assignment, o.Paths, o.Mechanism, o.CandidateId);
                valid = v.IsValid; context = v.ContextId; q = v.TotalTransfer; ds.AddRange(v.Diagnostics);
            }
            Check(valid && origin.ContextId == context, "CHILD_WHOLE_GRAPH_CONTEXT");
            Check(q == g.TargetTransfer, "EXACT_GOAL_TRANSFER"); actual = q;
            contexts.Add(TransmissionGoalContract.ContextId(plan.GoalId, origin.RequestId, candidate.CandidateId));
            var layerSet = origin.Mechanism.Spatial.Bodies.Select(b => b.Layer).Distinct().OrderBy(x => x).ToArray();
            Check(layerSet.SequenceEqual(Enumerable.Range(0, (int)origin.Family + 1)) && layerSet.Length <= g.MaximumLayerCount && layerSet.All(g.AvailableLayers.Contains) && g.OutputLayers.Contains((int)origin.Family), "ACTUAL_CANONICAL_LAYERS");
            Check((int)origin.Family <= g.MaximumCompounds && g.AllowedFamilies.Contains(origin.Family), "ACTUAL_TOPOLOGY_ENVELOPE");
        }
        if (ds.Any(d => d.Severity == DiagnosticSeverity.Error)) return new TransmissionGoalValidation(plan.GoalId, candidate.CandidateId, actual, contexts, ds);
        var m = Metrics(g, mechanism);
        Check(m.Canonical == candidate.Metrics.Canonical, "ACTUAL_COMMON_METRICS");
        Check(m.Compounds <= g.MaximumCompounds && m.Idlers <= g.MaximumTotalIdlers, "ACTUAL_RESOURCE_ENVELOPE");
        return new TransmissionGoalValidation(plan.GoalId, candidate.CandidateId, actual, contexts, ds);
    }

    public static TransmissionCommonMetrics Metrics(TransmissionGoal normalized, GenerationCandidate mechanism)
    {
        var axes = mechanism.Spatial.Axes.ToDictionary(a => a.Id, StringComparer.Ordinal); var bodies = mechanism.Spatial.Bodies;
        var groups = bodies.GroupBy(b => b.DofId, StringComparer.Ordinal).ToArray();
        var compounds = groups.Where(g => g.Count() == 2 && g.Select(b => b.ToothCount).Distinct().Count() == 2).ToArray();
        var idlers = groups.Where(g => g.Count() == 1).Select(g => g.Single()).Where(b => {
            var axis = axes[b.AxisId]; var p = new GearRoutePoint(axis.X, axis.Y); return !p.Equals(normalized.Input.Position) && !p.Equals(normalized.Output.Position);
        }).ToArray();
        BigInteger preferred = BigInteger.Zero;
        foreach (var body in idlers)
        {
            var a = axes[body.AxisId]; var box = normalized.Legs[body.Layer].PreferredRegion;
            if (box != null) preferred += GearRoutingGeometry.DistanceSquared(new GearRoutePoint(a.X, a.Y), box);
        }
        foreach (var group in compounds)
        {
            var body = group.OrderBy(b => b.Layer).First(); var box = normalized.Slots[body.Layer].PreferredRegion; var a = axes[body.AxisId];
            if (box != null) preferred += GearRoutingGeometry.DistanceSquared(new GearRoutePoint(a.X, a.Y), box);
        }
        var footprint = (bodies.Max(b => axes[b.AxisId].X + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].X - b.PitchRadius)) *
            (bodies.Max(b => axes[b.AxisId].Y + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].Y - b.PitchRadius));
        return new TransmissionCommonMetrics(compounds.Length, idlers.Length, preferred, footprint,
            bodies.Aggregate(BigInteger.Zero, (sum, b) => sum + b.ToothCount), bodies.Select(b => b.Layer));
    }

    public static int Compare(TransmissionMechanismCandidate a, TransmissionMechanismCandidate b)
    {
        int c = a.Metrics.Compounds.CompareTo(b.Metrics.Compounds); if (c != 0) return c;
        c = a.Metrics.Idlers.CompareTo(b.Metrics.Idlers); if (c != 0) return c;
        c = a.Metrics.PreferredPenalty.CompareTo(b.Metrics.PreferredPenalty); if (c != 0) return c;
        c = a.Metrics.Footprint.CompareTo(b.Metrics.Footprint); if (c != 0) return c;
        c = a.Metrics.TotalTeeth.CompareTo(b.Metrics.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.CandidateId, b.CandidateId);
    }

    // Exact bounded comparison independent of the candidate-ID hash. All current supported primitive fields
    // are included. Derived solution/playback are validated by the existing whole-graph validators.
    internal static string MechanicalSignature(GenerationCandidate c) => GearRoutingContract.Pack(c.Kinematic.RootDofId,
        GearRoutingContract.List(c.Kinematic.Dofs.Select(d => GearRoutingContract.Pack(d.Id, d.IsPrescribed ? "true" : "false"))),
        GearRoutingContract.List(c.Kinematic.Couplings.Select(k => GearRoutingContract.Pack(k.Id, k.DriverDofId, k.DrivenDofId, GearRoutingContract.Number(k.DriverTeeth), GearRoutingContract.Number(k.DrivenTeeth), k.PhaseOffset.ToString()))),
        GearRoutingContract.List(c.Spatial.Axes.Select(a => GearRoutingContract.Pack(a.Id, GearRoutingContract.Number(a.X), GearRoutingContract.Number(a.Y)))),
        GearRoutingContract.List(c.Spatial.Bodies.Select(b => GearRoutingContract.Pack(b.Id, b.Kind.ToString(), b.AxisId, b.DofId, GearRoutingContract.Number(b.Layer), GearRoutingContract.Number(b.ToothCount), GearRoutingContract.Number(b.PitchRadius), b.ExactMountingPhase.ToString()))),
        GearRoutingContract.List(c.Spatial.Contacts.Select(k => GearRoutingContract.Pack(k.Id, k.Kind.ToString(), k.ConstraintId, k.BodyAId, k.BodyBId))));
}

internal sealed class TransmissionCandidateCollector
{
    private readonly int maximumReturned;
    private readonly Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<TransmissionMechanismCandidate> retained = new List<TransmissionMechanismCandidate>();
    private int signatureCharacters;
    internal TransmissionCandidateCollector(int maximumReturned) { this.maximumReturned = maximumReturned; }
    internal IReadOnlyList<TransmissionMechanismCandidate> Candidates => retained;
    internal int UniqueCount => seen.Count;
    internal void Add(TransmissionMechanismCandidate c)
    {
        var signature = TransmissionGoalValidator.MechanicalSignature(c.Mechanism);
        if (seen.TryGetValue(c.CandidateId, out var prior))
        {
            if (prior != signature) throw new InvalidOperationException("SameCandidateIdDifferentMechanicalPayload");
            int index = retained.FindIndex(x => x.CandidateId == c.CandidateId);
            if (index >= 0)
            {
                var old = retained[index]; if (old.Metrics.Canonical != c.Metrics.Canonical) throw new InvalidOperationException("SameMechanismDifferentGoalScore");
                var origins = old.Origins.Concat(c.Origins).GroupBy(o => o.OriginId, StringComparer.Ordinal).Select(g => g.First());
                retained[index] = new TransmissionMechanismCandidate(c.CandidateId, origins, old.Metrics);
            }
            return;
        }
        if (seen.Count == TransmissionGoalContract.MaxUniqueObservations || signature.Length > 32768 || signatureCharacters + signature.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("GoalObservationResourceCeiling: unique IDs/signature characters; no global completeness claim");
        seen.Add(c.CandidateId, signature); signatureCharacters += signature.Length; retained.Add(c); retained.Sort(TransmissionGoalValidator.Compare);
        if (retained.Count > maximumReturned) retained.RemoveAt(retained.Count - 1);
    }
}

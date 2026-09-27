using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class TwoOutputTransmissionValidator
{
    public static ValidationBundle Validate(TwoOutputTransmissionGoal goal, TwoOutputTransmissionCandidate candidate, Func<GenerationCandidate, string> identity)
        => Validate(TwoOutputTransmissionCompiler.Compile(goal), candidate, identity);
    internal static ValidationBundle Validate(TwoOutputSharingSearchPlan plan, TwoOutputTransmissionCandidate c, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>();
        if (!plan.Normalized.IsValid) return new ValidationBundle(plan.Diagnostics.Concat(new[] { TwoOutputTransmissionCompiler.Error("INVALID_GOAL_CONTEXT", c.CandidateId, "No normalized goal.") }));
        string signature = TransmissionGoalValidator.MechanicalSignature(c.Mechanism);
        if (c.Origins.Select(o => o.OriginId).Distinct().Count() != c.Origins.Count) ds.Add(TwoOutputTransmissionCompiler.Error("DUPLICATE_ORIGIN", c.CandidateId, "Origins must be unique."));
        foreach (var origin in c.Origins)
        {
            ds.AddRange(ValidateOrigin(plan, origin, identity).Diagnostics);
            if (origin.CandidateId != c.CandidateId || TransmissionGoalValidator.MechanicalSignature(origin.Mechanism) != signature || origin.OutputBindingSignature != c.Origins[0].OutputBindingSignature)
                ds.Add(TwoOutputTransmissionCompiler.Error("ORIGIN_PAYLOAD_OR_BINDING_COLLISION", origin.OriginId, "Equal IDs require equal exact payload and semantic output ownership."));
            if (Metrics(plan.Normalized.Goal!, origin).Canonical != c.Metrics.Canonical)
                ds.Add(TwoOutputTransmissionCompiler.Error("COMMON_METRICS", origin.OriginId, "Common metrics must be recomputed from the actual whole mechanism."));
        }
        return new ValidationBundle(ds.Take(128));
    }
    internal static ValidationBundle ValidateOrigin(TwoOutputSharingSearchPlan plan, TwoOutputCandidateOrigin origin, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>(); var g = plan.Normalized.Goal!;
        void Check(bool ok, string code, string text) { if (!ok) ds.Add(TwoOutputTransmissionCompiler.Error(code, origin.OriginId, text)); }
        var strategy = plan.Strategies.Single(s => s.Strategy == origin.Strategy);
        Check(strategy.Applicability == TwoOutputStrategyApplicability.Eligible && strategy.Quota > 0, "ORIGIN_STRATEGY_COVERAGE", "Origin must belong to an active search-eligible strategy.");
        Check(origin.ChildGoalId == strategy.ChildGoalId, "CHILD_GOAL_CONTEXT", "Origin must use this compiled exact child request and quota.");
        if (ds.Count != 0) return new ValidationBundle(ds);
        if (origin.RootOnly != null) ds.AddRange(SharedDriverTransmissionComposer.Validate(strategy.RootOnly!.Normalized.Goal!, origin.RootOnly, identity).Diagnostics);
        else ds.AddRange(SharedPrefixTransmissionComposer.Validate(strategy.SharedPrefix!.Normalized.Goal!, origin.SharedPrefix!, identity).Diagnostics);
        if (ds.Any(d => d.Severity == DiagnosticSeverity.Error)) return new ValidationBundle(ds);
        var c = origin.Mechanism; var axes = c.Spatial.Axes.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var root = c.Spatial.Bodies.Single(b => b.DofId == c.Kinematic.RootDofId); var rootAxis = axes[root.AxisId];
        Check(root.Layer == 0 && root.ToothCount == g.Input.Teeth && new GearRoutePoint(rootAxis.X, rootAxis.Y).Equals(g.Input.Position), "ACTUAL_INPUT", "The fixed actual input cannot move/change teeth/layer.");
        Check(origin.Outputs.Select(o => o.DofId).Distinct().Count() == 2 && origin.Outputs.All(o => o.DofId != c.Kinematic.RootDofId), "DISTINCT_OUTPUT_BINDINGS", "Two distinct required output channels must remain.");
        Check(origin.Outputs.Select(o => o.OutputKey).SequenceEqual(g.Outputs.Select(o => o.Key)), "OUTPUT_KEYS", "Common stable output ownership must match.");
        foreach (var output in origin.Outputs)
        {
            var wanted = g.Outputs.Single(o => o.Key == output.OutputKey); var body = c.Spatial.Bodies.Single(b => b.Id == output.BodyId); var a = axes[body.AxisId];
            Check(new GearRoutePoint(a.X, a.Y).Equals(wanted.Anchor.Position) && body.ToothCount == wanted.Anchor.Teeth && wanted.OutputLayers.Contains(body.Layer), "FIXED_OUTPUT", "Common exact endpoint teeth/site/allowed layer must be preserved.");
            Check(c.Solution.States.Single(s => s.DofId == output.DofId).Coefficient == wanted.TargetTransfer && output.RequestedTransfer == wanted.TargetTransfer && output.ActualTransfer == wanted.TargetTransfer,
                "GLOBAL_OUTPUT_TARGET", "Read back global q from the whole solver, not a source-local target.");
        }
        SharedDriverTransmissionComposer.AddEnvironmentChecks(TwoOutputTransmissionCompiler.LowerRoot(g, 0), c.Spatial, ds);
        var endDofs = new HashSet<string>(origin.Outputs.Select(o => o.DofId).Concat(new[] { c.Kinematic.RootDofId }), StringComparer.Ordinal);
        foreach (var region in g.RequiredRegions) Check(c.Spatial.Bodies.Where(b => !endDofs.Contains(b.DofId)).Any(b => GearRoutingGeometry.Contains(region.Bounds, new GearRoutePoint(axes[b.AxisId].X, axes[b.AxisId].Y))),
            "GLOBAL_REQUIRED", "Any actual intermediate, including the shared splitter, can fulfill the global region.");
        var m = Metrics(g, origin);
        Check(m.Compounds <= g.MaximumTotalCompounds && m.BodyCount <= g.MaximumBodies && m.TransmissionGears <= g.MaximumTransmissionGears,
            "GLOBAL_LIMIT", "Common actual whole body/compound/transmission-gear limits must hold.");
        Check(m.Layers.All(g.AvailableLayers.Contains) && c.Spatial.Bodies.All(b => b.PitchRadius == b.ToothCount * g.PitchRadiusTicksPerTooth), "LAYER_AND_SCALE", "No layer remap or scale substitution.");
        return new ValidationBundle(ds.Take(64));
    }

    public static TwoOutputCommonMetrics Metrics(TwoOutputTransmissionGoal g, TwoOutputCandidateOrigin origin)
    {
        var c = origin.Mechanism; var bodies = c.Spatial.Bodies; var axes = c.Spatial.Axes.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var endpoints = new HashSet<string>(origin.Outputs.Select(o => o.DofId).Concat(new[] { c.Kinematic.RootDofId }), StringComparer.Ordinal);
        var groups = bodies.GroupBy(b => b.DofId, StringComparer.Ordinal).ToArray(); var intermediate = groups.Where(x => !endpoints.Contains(x.Key)).ToArray();
        int compounds = groups.Count(x => x.Count() == 2 && x.Select(b => b.ToothCount).Distinct().Count() == 2);
        BigInteger preference = BigInteger.Zero;
        foreach (var group in intermediate)
        {
            var body = group.OrderBy(b => b.Layer).First(); var a = axes[body.AxisId]; var point = new GearRoutePoint(a.X, a.Y);
            if (g.PreferredRegion != null) preference += GearRoutingGeometry.DistanceSquared(point, g.PreferredRegion);
            // Prefix-only route/splitter preferences stay source-local. The common Preferred is never doubled on the splitter.
            if (origin.SharedPrefix != null && group.Key == SharedPrefixTransmissionContract.SplitterDof) continue;
            foreach (var output in origin.Outputs)
            {
                if (!output.Mappings.Any(m => m.Kind == "dof" && m.MergedId == group.Key)) continue;
                var domain = g.Outputs.Single(o => o.Key == output.OutputKey);
                var box = group.Count() == 1 ? domain.Legs[body.Layer].PreferredRegion : domain.Slots[body.Layer].PreferredRegion;
                if (box != null) preference += GearRoutingGeometry.DistanceSquared(point, box);
            }
        }
        var footprint = (bodies.Max(b => axes[b.AxisId].X + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].X - b.PitchRadius)) *
            (bodies.Max(b => axes[b.AxisId].Y + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].Y - b.PitchRadius));
        return new TwoOutputCommonMetrics(compounds, bodies.Count, intermediate.Sum(x => x.Count()), intermediate.Count(x => x.Count() == 1), footprint,
            preference, bodies.Aggregate(BigInteger.Zero, (sum, b) => sum + b.ToothCount), bodies.Select(b => b.Layer));
    }
    public static int Compare(TwoOutputTransmissionCandidate a, TwoOutputTransmissionCandidate b)
    {
        int c = a.Metrics.Compounds.CompareTo(b.Metrics.Compounds); if (c != 0) return c;
        c = a.Metrics.NonEndpointBodies.CompareTo(b.Metrics.NonEndpointBodies); if (c != 0) return c;
        c = a.Metrics.Footprint.CompareTo(b.Metrics.Footprint); if (c != 0) return c;
        c = a.Metrics.PreferredPenalty.CompareTo(b.Metrics.PreferredPenalty); if (c != 0) return c;
        c = a.Metrics.TotalTeeth.CompareTo(b.Metrics.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.CandidateId, b.CandidateId);
    }
}

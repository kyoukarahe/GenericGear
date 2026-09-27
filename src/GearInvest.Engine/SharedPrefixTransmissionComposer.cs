using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class SharedPrefixTransmissionComposer
{
    public static SharedPrefixCompositionResult Compose(SharedPrefixTransmissionGoal goal, string hypothesisId, TransmissionMechanismCandidate prefix,
        TransmissionMechanismCandidate left, TransmissionMechanismCandidate right, Func<GenerationCandidate, string> identity)
        => Compose(SharedPrefixTransmissionCompiler.Compile(goal), hypothesisId, prefix, left, right, identity);

    internal static SharedPrefixCompositionResult Compose(SharedPrefixTransmissionSearchPlan plan, string hypothesisId, TransmissionMechanismCandidate prefix,
        TransmissionMechanismCandidate left, TransmissionMechanismCandidate right, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>(); var h = plan.Hypotheses.SingleOrDefault(x => x.HypothesisId == hypothesisId);
        if (!plan.IsSupported || h?.Prefix == null || h.Suffix == null)
            return new SharedPrefixCompositionResult(null, new[] { SharedPrefixTransmissionCompiler.Error("HYPOTHESIS_CONTEXT", hypothesisId, "Supported original-input prefix and derived suffix context required.") });
        ds.AddRange(TransmissionGoalValidator.Validate(h.Prefix, prefix, identity).Diagnostics);
        if (ds.Count != 0) return new SharedPrefixCompositionResult(null, ds);
        // Reuse 16B's actual primitive suffix-pair merge/validation, not a retained pair list or pair-search result.
        var pair = SharedDriverTransmissionComposer.Compose(h.Suffix, left, right, identity); ds.AddRange(pair.Diagnostics);
        if (!pair.IsValid) return new SharedPrefixCompositionResult(null, ds);
        var g = plan.Normalized.Goal!; var maps = PrefixMap(prefix.Mechanism); var spec = Build(prefix.Mechanism, pair.Candidate!.Mechanism, maps);
        SharedDriverTransmissionComposer.AddEnvironmentChecks(SharedPrefixTransmissionCompiler.EndpointDomain(g), spec.Spatial, ds);
        if (ds.Count != 0) return new SharedPrefixCompositionResult(null, ds);
        var generated = new GenerationEngine().Generate(spec); ds.AddRange(generated.Diagnostics);
        if (!generated.IsSuccess) return new SharedPrefixCompositionResult(null, ds);
        var c = generated.Candidates.Single(); var r = Coefficient(c, SharedPrefixTransmissionContract.SplitterDof);
        var outputs = pair.Candidate.Outputs.Select(o => new SharedDriverOutputBinding(o.OutputKey, o.ChildGoalId, o.Child, o.AxisId, o.DofId, o.BodyId,
            g.Outputs.Single(x => x.Key == o.OutputKey).TargetTransfer, Coefficient(c, o.DofId), o.Mappings.Select(m => new SharedDriverRoleMapping(m.Kind, m.LocalId, RemapSuffix(m.MergedId))))).ToArray();
        var candidate = new SharedPrefixTransmissionCandidate(identity(c), hypothesisId, prefix, c, r, maps, outputs, Metrics(g, c, prefix, outputs));
        ds.AddRange(ValidateWhole(g, h, candidate).Diagnostics);
        return new SharedPrefixCompositionResult(ds.Any(d => d.Severity == DiagnosticSeverity.Error) ? null : candidate, ds);
    }

    internal static Rational Coefficient(GenerationCandidate c, string dof) => c.Solution.States.Single(s => s.DofId == dof).Coefficient;
    private static string RemapSuffix(string id) => id == SharedDriverTransmissionContract.RootDof ? SharedPrefixTransmissionContract.SplitterDof :
        id == SharedDriverTransmissionContract.RootAxis ? SharedPrefixTransmissionContract.SplitterAxis : id == SharedDriverTransmissionContract.RootBody ? SharedPrefixTransmissionContract.SplitterBody : id;
    private static SharedDriverRoleMapping[] PrefixMap(GenerationCandidate p)
    {
        var map = new List<SharedDriverRoleMapping>(); var root = p.Kinematic.RootDofId;
        var terminal = p.Kinematic.Dofs.Single(d => !p.Kinematic.Couplings.Any(k => k.DriverDofId == d.Id)).Id;
        var inputBody = p.Spatial.Bodies.Single(b => b.DofId == root); var terminalBody = p.Spatial.Bodies.Single(b => b.DofId == terminal);
        string Role(string kind, string id, string input, string output) => id == input ? kind + ":actual-input" : id == output ? kind + ":shared-splitter" : kind + ":shared-prefix:" + id;
        map.AddRange(p.Kinematic.Dofs.Select(d => new SharedDriverRoleMapping("dof", d.Id, Role("dof", d.Id, root, terminal))));
        map.AddRange(p.Spatial.Axes.Select(a => new SharedDriverRoleMapping("axis", a.Id, Role("axis", a.Id, inputBody.AxisId, terminalBody.AxisId))));
        map.AddRange(p.Spatial.Bodies.Select(b => new SharedDriverRoleMapping("body", b.Id, Role("body", b.Id, inputBody.Id, terminalBody.Id))));
        map.AddRange(p.Kinematic.Couplings.Select(k => new SharedDriverRoleMapping("mesh", k.Id, "mesh:shared-prefix:" + k.Id)));
        map.AddRange(p.Spatial.Contacts.Select(t => new SharedDriverRoleMapping("contact", t.Id, "contact:shared-prefix:" + t.Id)));
        return map.OrderBy(m => m.Kind, StringComparer.Ordinal).ThenBy(m => m.LocalId, StringComparer.Ordinal).ToArray();
    }
    private static LowLevelMechanicalSpecification Build(GenerationCandidate p, GenerationCandidate suffix, IReadOnlyList<SharedDriverRoleMapping> mappings)
    {
        string Map(string kind, string id) => mappings.Single(m => m.Kind == kind && m.LocalId == id).MergedId;
        var dofs = p.Kinematic.Dofs.Select(d => new RotationalDof(Map("dof", d.Id), d.Id == p.Kinematic.RootDofId))
            .Concat(suffix.Kinematic.Dofs.Where(d => d.Id != suffix.Kinematic.RootDofId).Select(d => new RotationalDof(RemapSuffix(d.Id), false)));
        var axes = p.Spatial.Axes.Select(a => new SpatialAxis(Map("axis", a.Id), a.X, a.Y))
            .Concat(suffix.Spatial.Axes.Where(a => a.Id != SharedDriverTransmissionContract.RootAxis));
        var bodies = p.Spatial.Bodies.Select(b => new SpatialBody(Map("body", b.Id), b.Kind, Map("axis", b.AxisId), Map("dof", b.DofId), b.Layer, b.ToothCount, b.PitchRadius, b.ExactMountingPhase))
            .Concat(suffix.Spatial.Bodies.Where(b => b.Id != SharedDriverTransmissionContract.RootBody));
        var couplings = p.Kinematic.Couplings.Select(k => new ExternalGearCoupling(Map("mesh", k.Id), Map("dof", k.DriverDofId), Map("dof", k.DrivenDofId), k.DriverTeeth, k.DrivenTeeth, k.PhaseOffset))
            .Concat(suffix.Kinematic.Couplings.Select(k => new ExternalGearCoupling(k.Id, RemapSuffix(k.DriverDofId), RemapSuffix(k.DrivenDofId), k.DriverTeeth, k.DrivenTeeth, k.PhaseOffset)));
        var contacts = p.Spatial.Contacts.Select(t => new SpatialContact(Map("contact", t.Id), t.Kind, Map("mesh", t.ConstraintId), Map("body", t.BodyAId), Map("body", t.BodyBId)))
            .Concat(suffix.Spatial.Contacts.Select(t => new SpatialContact(t.Id, t.Kind, t.ConstraintId, RemapSuffix(t.BodyAId), RemapSuffix(t.BodyBId))));
        return new LowLevelMechanicalSpecification(SharedPrefixTransmissionContract.Profile, new KinematicSpecification(SharedPrefixTransmissionContract.RootDof, dofs, couplings), new SpatialMechanism(axes, bodies, contacts));
    }

    private static ValidationBundle ValidateWhole(SharedPrefixTransmissionGoal g, SharedPrefixHypothesis h, SharedPrefixTransmissionCandidate c)
    {
        var ds = new List<Diagnostic>();
        void Check(bool ok, string code) { if (!ok) ds.Add(SharedPrefixTransmissionCompiler.Error(code, c.CandidateId, code)); }
        var m = c.Mechanism; var k = m.Kinematic; var s = m.Spatial; var prefix = c.Prefix.Mechanism;
        ds.AddRange(new GenerationEngine().Validate(m, new SpatialValidationOptions(g.PitchRadiusTicksPerTooth, 3, g.UnrelatedClearance)).Diagnostics);
        SharedDriverTransmissionComposer.AddEnvironmentChecks(SharedPrefixTransmissionCompiler.EndpointDomain(g), s, ds);
        Check(k.RootDofId == SharedPrefixTransmissionContract.RootDof && k.Dofs.Count(d => d.IsPrescribed) == 1 && k.Dofs.Single(d => d.IsPrescribed).Id == k.RootDofId, "ONE_ACTUAL_DRIVER");
        Check(k.Couplings.Count(x => x.DriverDofId == k.RootDofId) == 1 && !k.Couplings.Any(x => x.DrivenDofId == k.RootDofId), "INPUT_STARTS_NONEMPTY_PREFIX");
        Check(k.Dofs.Count(d => d.Id == SharedPrefixTransmissionContract.SplitterDof && !d.IsPrescribed) == 1 && s.Axes.Count(a => a.Id == SharedPrefixTransmissionContract.SplitterAxis) == 1 &&
            s.Bodies.Count(b => b.Id == SharedPrefixTransmissionContract.SplitterBody && b.DofId == SharedPrefixTransmissionContract.SplitterDof && b.AxisId == SharedPrefixTransmissionContract.SplitterAxis && b.Layer == 0 && b.ToothCount == h.Splitter.Teeth) == 1, "ONE_NONDRIVEN_SPLITTER_BODY_AXIS_DOF");
        var sa = s.Axes.Single(a => a.Id == SharedPrefixTransmissionContract.SplitterAxis);
        Check(sa.X == h.Splitter.Position.X && sa.Y == h.Splitter.Position.Y && !h.Splitter.Position.Equals(g.Input.Position) && !g.Outputs.Any(o => o.Anchor.Position.Equals(h.Splitter.Position)), "SPLITTER_POSE_ROLE");
        Check(k.Couplings.Count(x => x.DrivenDofId == SharedPrefixTransmissionContract.SplitterDof) == 1 && k.Couplings.Count(x => x.DriverDofId == SharedPrefixTransmissionContract.SplitterDof) == 2 &&
            s.Contacts.Count(t => t.BodyAId == SharedPrefixTransmissionContract.SplitterBody || t.BodyBId == SharedPrefixTransmissionContract.SplitterBody) == 3, "SPLITTER_THREE_REAL_CONTACTS");
        Check(k.Dofs.Count == prefix.Kinematic.Dofs.Count + c.Outputs.Sum(o => o.Child.Mechanism.Kinematic.Dofs.Count) - 2 &&
            s.Axes.Count == k.Dofs.Count && s.Bodies.Count == prefix.Spatial.Bodies.Count + c.Outputs.Sum(o => o.Child.Mechanism.Spatial.Bodies.Count) - 2 &&
            s.Contacts.Count == prefix.Spatial.Contacts.Count + c.Outputs.Sum(o => o.Child.Mechanism.Spatial.Contacts.Count) && k.Couplings.Count == s.Contacts.Count, "ONE_PREFIX_EXACT_UNION_COUNTS");
        var solved = KinematicSolver.Solve(k).Solution!; Rational Read(string id) => solved.States.Single(x => x.DofId == id).Coefficient;
        Check(Read(k.RootDofId) == Rational.One && Read(SharedPrefixTransmissionContract.SplitterDof) == h.Transfer && c.SplitterTransfer == h.Transfer, "ACTUAL_PREFIX_TRANSFER");
        Check(solved.States.All(x => x.PhaseOffset == Rational.Zero) && s.Bodies.All(b => b.ExactMountingPhase == Rational.Zero), "WHOLE_ZERO_PHASE");
        Check(c.Outputs.Select(o => o.OutputKey).SequenceEqual(g.Outputs.Select(o => o.Key)) && c.Outputs.Select(o => o.DofId).Distinct().Count() == 2, "TWO_DISTINCT_OUTPUT_BINDINGS");
        foreach (var output in c.Outputs)
        {
            var expected = g.Outputs.Single(o => o.Key == output.OutputKey); var local = h.Suffix!.Branches.Single(b => b.OutputKey == output.OutputKey).Plan.Normalized.Goal!.TargetTransfer;
            Check(Read(output.DofId) == expected.TargetTransfer && Read(output.DofId) / Read(SharedPrefixTransmissionContract.SplitterDof) == local &&
                output.ActualTransfer == expected.TargetTransfer && output.RequestedTransfer == expected.TargetTransfer, "ACTUAL_LOCAL_AND_GLOBAL_TARGETS");
            Check(!k.Couplings.Any(x => x.DriverDofId == output.DofId), "TERMINAL_OUTPUT");
        }
        Check(c.Metrics.Compounds <= g.MaximumTotalCompounds && c.PrefixIdlers + c.SuffixIdlers <= g.MaximumTotalIdlers && s.Bodies.Count <= g.MaximumBodies, "GLOBAL_TOPOLOGY_BODY_LIMIT");
        Check(c.PrefixIdlers >= g.Prefix.MinIdlers && c.PrefixIdlers <= g.Prefix.MaxIdlers, "PREFIX_DEPTH");
        return new ValidationBundle(ds.Take(128));
    }

    public static ValidationBundle Validate(SharedPrefixTransmissionGoal goal, SharedPrefixTransmissionCandidate c, Func<GenerationCandidate, string> identity)
        => Validate(SharedPrefixTransmissionCompiler.Compile(goal), c, identity);
    internal static ValidationBundle Validate(SharedPrefixTransmissionSearchPlan plan, SharedPrefixTransmissionCandidate c, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>();
        if (c.Outputs.Count != 2) return new ValidationBundle(new[] { SharedPrefixTransmissionCompiler.Error("OUTPUT_COUNT", c.CandidateId, "Two required outputs.") });
        var rebuilt = Compose(plan, c.HypothesisId, c.Prefix, c.Outputs[0].Child, c.Outputs[1].Child, identity); ds.AddRange(rebuilt.Diagnostics);
        if (!rebuilt.IsValid) return new ValidationBundle(ds);
        var expected = rebuilt.Candidate!;
        if (c.CandidateId != identity(c.Mechanism) || c.Mechanism.SourceId != SharedPrefixTransmissionContract.Profile ||
            TransmissionGoalValidator.MechanicalSignature(c.Mechanism) != TransmissionGoalValidator.MechanicalSignature(expected.Mechanism) || c.ContextId(plan.GoalId) != expected.ContextId(plan.GoalId))
            ds.Add(SharedPrefixTransmissionCompiler.Error("EXACT_SHARED_PREFIX_CONTEXT_REMAP", c.CandidateId, "Rehashed wrong driver, role, part, metric, binding, solution or playback is not a valid whole artifact."));
        ds.AddRange(new GenerationEngine().Validate(c.Mechanism).Diagnostics);
        return new ValidationBundle(ds.Take(128));
    }

    public static TransmissionCommonMetrics Metrics(SharedPrefixTransmissionGoal g, GenerationCandidate c, TransmissionMechanismCandidate prefix, IReadOnlyList<SharedDriverOutputBinding> outputs)
    {
        var axes = c.Spatial.Axes.ToDictionary(a => a.Id, StringComparer.Ordinal); var bodies = c.Spatial.Bodies;
        var ends = new HashSet<string>(outputs.Select(o => o.DofId).Concat(new[] { c.Kinematic.RootDofId }), StringComparer.Ordinal);
        var groups = bodies.GroupBy(b => b.DofId, StringComparer.Ordinal).ToArray(); var intermediates = groups.Where(x => !ends.Contains(x.Key)).ToArray();
        int compounds = groups.Count(x => x.Count() == 2 && x.Select(b => b.ToothCount).Distinct().Count() == 2); int transmissionGears = intermediates.Count(x => x.Count() == 1);
        BigInteger penalty = BigInteger.Zero;
        var prefixDofs = new HashSet<string>(PrefixMap(prefix.Mechanism).Where(m => m.Kind == "dof").Select(m => m.MergedId), StringComparer.Ordinal);
        foreach (var group in intermediates)
        {
            var body = group.OrderBy(b => b.Layer).First(); var a = axes[body.AxisId]; var point = new GearRoutePoint(a.X, a.Y);
            if (prefixDofs.Contains(group.Key) && group.Key != SharedPrefixTransmissionContract.SplitterDof && g.Prefix.PreferredRegion != null)
                penalty += GearRoutingGeometry.DistanceSquared(point, g.Prefix.PreferredRegion);
            foreach (var output in outputs)
            {
                if (group.Key == SharedPrefixTransmissionContract.SplitterDof || !output.Mappings.Any(m => m.Kind == "dof" && m.MergedId == group.Key)) continue;
                var domain = g.Outputs.Single(o => o.Key == output.OutputKey);
                var preferred = group.Count() == 1 ? domain.Legs[body.Layer].PreferredRegion : domain.Slots[body.Layer].PreferredRegion;
                if (preferred != null) penalty += GearRoutingGeometry.DistanceSquared(point, preferred);
            }
        }
        if (g.PreferredSplitterRegion != null) { var a = axes[SharedPrefixTransmissionContract.SplitterAxis]; penalty += GearRoutingGeometry.DistanceSquared(new GearRoutePoint(a.X, a.Y), g.PreferredSplitterRegion); }
        if (g.PreferredRegion != null) foreach (var group in intermediates) { var a = axes[group.First().AxisId]; penalty += GearRoutingGeometry.DistanceSquared(new GearRoutePoint(a.X, a.Y), g.PreferredRegion); }
        var footprint = (bodies.Max(b => axes[b.AxisId].X + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].X - b.PitchRadius)) *
            (bodies.Max(b => axes[b.AxisId].Y + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].Y - b.PitchRadius));
        return new TransmissionCommonMetrics(compounds, transmissionGears, penalty, footprint, bodies.Aggregate(BigInteger.Zero, (sum, b) => sum + b.ToothCount), bodies.Select(b => b.Layer));
    }
    public static int Compare(SharedPrefixTransmissionCandidate a, SharedPrefixTransmissionCandidate b)
    {
        int c = a.Metrics.Compounds.CompareTo(b.Metrics.Compounds); if (c != 0) return c;
        c = a.Metrics.Idlers.CompareTo(b.Metrics.Idlers); if (c != 0) return c;
        c = a.Metrics.PreferredPenalty.CompareTo(b.Metrics.PreferredPenalty); if (c != 0) return c;
        c = a.Metrics.Footprint.CompareTo(b.Metrics.Footprint); if (c != 0) return c;
        c = a.Metrics.TotalTeeth.CompareTo(b.Metrics.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.CandidateId, b.CandidateId);
    }
}

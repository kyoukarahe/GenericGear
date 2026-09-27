using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class SharedDriverTransmissionComposer
{
    private sealed class BranchMap
    {
        internal string Key = "", ChildGoalId = "", RootBody = "", RootAxis = "", OutputBody = "", OutputDof = "", OutputAxis = "";
        internal TransmissionMechanismCandidate Child = null!;
        internal readonly List<SharedDriverRoleMapping> Mappings = new List<SharedDriverRoleMapping>();
        internal string Map(string kind, string id) => Mappings.Single(m => m.Kind == kind && m.LocalId == id).MergedId;
        internal SharedDriverOutputBinding Binding(SharedDriverTransmissionOutput goal, KinematicSolution solution) => new SharedDriverOutputBinding(Key, ChildGoalId, Child,
            Map("axis", OutputAxis), Map("dof", OutputDof), Map("body", OutputBody), goal.TargetTransfer,
            solution.States.Single(s => s.DofId == Map("dof", OutputDof)).Coefficient, Mappings);
    }

    public static SharedDriverPairResult Compose(SharedDriverTransmissionGoal goal, TransmissionMechanismCandidate left, TransmissionMechanismCandidate right,
        Func<GenerationCandidate, string> identity) => Compose(SharedDriverTransmissionCompiler.Compile(goal), left, right, identity);

    internal static SharedDriverPairResult Compose(SharedDriverTransmissionSearchPlan plan, TransmissionMechanismCandidate left, TransmissionMechanismCandidate right, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>();
        var maps = BuildMaps(plan, new[] { left, right }, identity, ds);
        if (maps == null) return new SharedDriverPairResult(null, ds);
        var spec = Build(maps); var g = plan.Normalized.Goal!;
        ds.AddRange(SpatialChecks(g, spec.Spatial, spec.Kinematic));
        if (ds.Count != 0) return new SharedDriverPairResult(null, ds);
        // No child-local coefficients or playback enter generation. The root fan-out graph is solved afresh.
        var generated = new GenerationEngine().Generate(spec); ds.AddRange(generated.Diagnostics);
        if (!generated.IsSuccess) return new SharedDriverPairResult(null, ds);
        var mechanism = generated.Candidates.Single(); var bindings = maps.Select(m => m.Binding(g.Outputs.Single(o => o.Key == m.Key), mechanism.Solution)).ToArray();
        var candidate = new SharedDriverTransmissionCandidate(identity(mechanism), mechanism, bindings, Metrics(g, mechanism, bindings));
        ds.AddRange(Validate(plan, candidate, identity).Diagnostics);
        return new SharedDriverPairResult(ds.Any(d => d.Severity == DiagnosticSeverity.Error) ? null : candidate, ds);
    }

    public static ValidationBundle Validate(SharedDriverTransmissionGoal goal, SharedDriverTransmissionCandidate candidate, Func<GenerationCandidate, string> identity)
        => Validate(SharedDriverTransmissionCompiler.Compile(goal), candidate, identity);

    internal static ValidationBundle Validate(SharedDriverTransmissionSearchPlan plan, SharedDriverTransmissionCandidate c, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>();
        void Check(bool ok, string code, string subject = "mechanism") { if (!ok) ds.Add(SharedDriverTransmissionCompiler.Error(code, subject, code)); }
        if (!plan.IsSupported) { ds.AddRange(plan.Diagnostics); Check(false, "UNSUPPORTED_GOAL"); return new ValidationBundle(ds); }
        Check(c.Outputs.Select(o => o.OutputKey).SequenceEqual(plan.Branches.Select(b => b.OutputKey)), "TWO_OUTPUT_KEYS");
        if (ds.Count != 0) return new ValidationBundle(ds);
        var maps = BuildMaps(plan, c.Outputs.Select(o => o.Child).ToArray(), identity, ds);
        if (maps == null) return new ValidationBundle(ds);
        var expected = Build(maps); var g = plan.Normalized.Goal!;
        // Compare the exact primitive remap as well as the hash. This catches omitted/extra contacts, accidental sharing and foreign branches.
        var generated = new GenerationEngine().Generate(expected); ds.AddRange(generated.Diagnostics);
        if (!generated.IsSuccess) return new ValidationBundle(ds);
        var expectedMechanism = generated.Candidates.Single();
        Check(TransmissionGoalValidator.MechanicalSignature(c.Mechanism) == TransmissionGoalValidator.MechanicalSignature(expectedMechanism), "WHOLE_ROOT_ONLY_REMAP");
        Check(c.CandidateId == identity(c.Mechanism), "MECHANICAL_IDENTITY");
        Check(c.Mechanism.SourceId == SharedDriverTransmissionContract.Profile, "SOURCE_PROFILE");
        ds.AddRange(new GenerationEngine().Validate(c.Mechanism, new SpatialValidationOptions(g.PitchRadiusTicksPerTooth, 3, g.UnrelatedClearance)).Diagnostics);
        ds.AddRange(SpatialChecks(g, c.Mechanism.Spatial, c.Mechanism.Kinematic));
        for (int i = 0; i < maps.Length; i++)
        {
            var binding = maps[i].Binding(g.Outputs[i], expectedMechanism.Solution);
            Check(binding.Canonical == c.Outputs[i].Canonical, "OUTPUT_ROLE_OR_ORIGIN_BINDING", g.Outputs[i].Key);
            Check(binding.ActualTransfer == g.Outputs[i].TargetTransfer, "EXACT_TARGET_TRANSFER", g.Outputs[i].Key);
        }
        var actual = Metrics(g, expectedMechanism, c.Outputs);
        Check(actual.Canonical == c.Metrics.Canonical, "MERGED_METRICS");
        Check(actual.Compounds <= g.MaximumTotalCompounds && actual.Idlers <= g.MaximumTotalIdlers, "GLOBAL_TOPOLOGY_LIMIT");
        return new ValidationBundle(ds.Take(128));
    }

    private static BranchMap[]? BuildMaps(SharedDriverTransmissionSearchPlan plan, IReadOnlyList<TransmissionMechanismCandidate> children, Func<GenerationCandidate, string> identity, List<Diagnostic> ds)
    {
        if (!plan.IsSupported || children.Count != 2 || plan.Branches.Count != 2)
        { ds.Add(SharedDriverTransmissionCompiler.Error("PAIR_CONTEXT", "pair", "Two supported required branch contexts are necessary.")); return null; }
        var maps = new List<BranchMap>(); var g = plan.Normalized.Goal!;
        for (int i = 0; i < 2; i++)
        {
            var child = children[i]; var branch = plan.Branches[i]; var v = TransmissionGoalValidator.Validate(branch.Plan, child, identity);
            if (!v.IsValid) { ds.AddRange(v.Diagnostics); continue; }
            var c = child.Mechanism; var root = c.Kinematic.RootDofId;
            var roots = c.Spatial.Bodies.Where(b => b.DofId == root).ToArray();
            // A validated serial branch has one root body and one sink. Discover by graph roles, not substring names.
            var sinks = c.Kinematic.Dofs.Where(d => !c.Kinematic.Couplings.Any(k => k.DriverDofId == d.Id)).ToArray();
            if (roots.Length != 1 || sinks.Length != 1 || c.Kinematic.Dofs.Count(d => d.IsPrescribed) != 1 || c.Kinematic.Couplings.Count(k => k.DriverDofId == root) != 1)
            { ds.Add(SharedDriverTransmissionCompiler.Error("ROOT_OR_OUTPUT_ROLE", branch.OutputKey, "Expected one driver, one root body/first contact and one sink.")); continue; }
            var body = roots[0]; var axis = c.Spatial.Axes.Single(a => a.Id == body.AxisId); var outputBody = c.Spatial.Bodies.Single(b => b.DofId == sinks[0].Id);
            if (axis.X != g.Input.Position.X || axis.Y != g.Input.Position.Y || body.ToothCount != g.Input.Teeth || body.PitchRadius != g.Input.Teeth * g.PitchRadiusTicksPerTooth ||
                body.Layer != 0 || body.Kind != SpatialBodyKind.Gear || body.ExactMountingPhase != Rational.Zero || c.Kinematic.Couplings.Any(k => k.PhaseOffset != Rational.Zero))
            { ds.Add(SharedDriverTransmissionCompiler.Error("SHARED_INPUT_COMPATIBILITY", branch.OutputKey, "Input exact position, teeth, scale, plane, fixed-axis profile and zero phase must agree without correction.")); continue; }
            var map = new BranchMap { Key = branch.OutputKey, ChildGoalId = branch.Plan.GoalId, Child = child, RootBody = body.Id, RootAxis = body.AxisId,
                OutputBody = outputBody.Id, OutputDof = outputBody.DofId, OutputAxis = outputBody.AxisId };
            void Add(string kind, string id, string? shared = null) => map.Mappings.Add(new SharedDriverRoleMapping(kind, id, shared ?? kind + ":shared-output:" + branch.OutputKey + ":" + id));
            foreach (var d in c.Kinematic.Dofs) Add("dof", d.Id, d.Id == root ? SharedDriverTransmissionContract.RootDof : null);
            foreach (var a in c.Spatial.Axes) Add("axis", a.Id, a.Id == body.AxisId ? SharedDriverTransmissionContract.RootAxis : null);
            foreach (var b in c.Spatial.Bodies) Add("body", b.Id, b.Id == body.Id ? SharedDriverTransmissionContract.RootBody : null);
            foreach (var k in c.Kinematic.Couplings) Add("mesh", k.Id);
            foreach (var t in c.Spatial.Contacts) Add("contact", t.Id);
            maps.Add(map);
        }
        return maps.Count == 2 && ds.Count == 0 ? maps.ToArray() : null;
    }

    private static LowLevelMechanicalSpecification Build(IReadOnlyList<BranchMap> maps)
    {
        var dofs = new List<RotationalDof>(); var axes = new List<SpatialAxis>(); var bodies = new List<SpatialBody>(); var meshes = new List<ExternalGearCoupling>(); var contacts = new List<SpatialContact>();
        for (int i = 0; i < maps.Count; i++)
        {
            var m = maps[i]; var c = m.Child.Mechanism;
            dofs.AddRange(c.Kinematic.Dofs.Where(d => i == 0 || d.Id != c.Kinematic.RootDofId).Select(d => new RotationalDof(m.Map("dof", d.Id), d.IsPrescribed)));
            axes.AddRange(c.Spatial.Axes.Where(a => i == 0 || a.Id != m.RootAxis).Select(a => new SpatialAxis(m.Map("axis", a.Id), a.X, a.Y)));
            bodies.AddRange(c.Spatial.Bodies.Where(b => i == 0 || b.Id != m.RootBody).Select(b => new SpatialBody(m.Map("body", b.Id), b.Kind, m.Map("axis", b.AxisId), m.Map("dof", b.DofId), b.Layer, b.ToothCount, b.PitchRadius, b.ExactMountingPhase)));
            meshes.AddRange(c.Kinematic.Couplings.Select(k => new ExternalGearCoupling(m.Map("mesh", k.Id), m.Map("dof", k.DriverDofId), m.Map("dof", k.DrivenDofId), k.DriverTeeth, k.DrivenTeeth, k.PhaseOffset)));
            contacts.AddRange(c.Spatial.Contacts.Select(t => new SpatialContact(m.Map("contact", t.Id), t.Kind, m.Map("mesh", t.ConstraintId), m.Map("body", t.BodyAId), m.Map("body", t.BodyBId))));
        }
        return new LowLevelMechanicalSpecification(SharedDriverTransmissionContract.Profile, new KinematicSpecification(SharedDriverTransmissionContract.RootDof, dofs, meshes), new SpatialMechanism(axes, bodies, contacts));
    }

    internal static IEnumerable<Diagnostic> SpatialChecks(SharedDriverTransmissionGoal g, SpatialMechanism spatial, KinematicSpecification kinematic)
    {
        var ds = new List<Diagnostic>();
        void Check(bool ok, string code, string subject, string detail) { if (!ok && ds.Count < 64) ds.Add(SharedDriverTransmissionCompiler.Error(code, subject, detail)); }
        Check(kinematic.RootDofId == SharedDriverTransmissionContract.RootDof && kinematic.Dofs.Count(d => d.IsPrescribed) == 1 &&
            kinematic.Dofs.Any(d => d.Id == SharedDriverTransmissionContract.RootDof && d.IsPrescribed) && spatial.Bodies.Count(b => b.DofId == SharedDriverTransmissionContract.RootDof) == 1,
            "SINGLE_ROOT_DRIVER_BODY", "input", "Exactly one root DOF/axis/body/driver must remain.");
        Check(kinematic.Couplings.Count(k => k.DriverDofId == SharedDriverTransmissionContract.RootDof) == 2 &&
            spatial.Contacts.Count(t => t.BodyAId == SharedDriverTransmissionContract.RootBody || t.BodyBId == SharedDriverTransmissionContract.RootBody) == 2,
            "SHARED_ROOT_FANOUT", "input", "Both first contacts must reference the same root body.");
        AddEnvironmentChecks(g, spatial, ds);
        return ds;
    }

    // Shared exact environment checks; no root/fanout semantics. 16B retains its original checks/order/cap above.
    internal static void AddEnvironmentChecks(SharedDriverTransmissionGoal g, SpatialMechanism spatial, List<Diagnostic> ds)
    {
        void Check(bool ok, string code, string subject, string detail) { if (!ok && ds.Count < 64) ds.Add(SharedDriverTransmissionCompiler.Error(code, subject, detail)); }
        var axes = spatial.Axes.GroupBy(a => a.Id).ToDictionary(p => p.Key, p => p.First(), StringComparer.Ordinal);
        foreach (var group in spatial.Axes.GroupBy(a => GearRoutingContract.Pack(GearRoutingContract.Number(a.X), GearRoutingContract.Number(a.Y))).Where(x => x.Count() > 1))
            Check(false, "DISTINCT_AXIS_POSITION", string.Join("|", group.Select(a => a.Id)), "Non-root axis collision at " + group.First().X + "," + group.First().Y + "; no prefix/output/intermediate union.");
        var endpointPositions = g.Outputs.Select(o => o.Anchor.Position).Concat(new[] { g.Input.Position }).ToArray();
        foreach (var body in spatial.Bodies)
        {
            if (!axes.TryGetValue(body.AxisId, out var a)) { Check(false, "UNKNOWN_AXIS", body.Id, "Unknown axis reference."); continue; }
            var p = new GearRoutePoint(a.X, a.Y);
            Check(g.AvailableLayers.Contains(body.Layer), "GLOBAL_LAYER", body.Id, "Unavailable physical layer.");
            Check(GearRoutingGeometry.ContainsDisk(g.Bounds, p, body.PitchRadius), "GLOBAL_BOUNDS", body.Id, "Full pitch disk outside global bounds.");
            foreach (var k in g.KeepOuts.Where(k => k.AppliesTo(body.Layer))) Check(GearRoutingGeometry.ClearsKeepOut(p, body.PitchRadius, k.Bounds, g.KeepOutClearance), "GLOBAL_KEEPOUT", body.Id, k.Id);
        }
        foreach (var region in g.RequiredRegions) Check(spatial.Axes.Any(a => !endpointPositions.Contains(new GearRoutePoint(a.X, a.Y)) && GearRoutingGeometry.Contains(region.Bounds, new GearRoutePoint(a.X, a.Y))),
            "GLOBAL_REQUIRED", region.Id, "No intermediate axis center visits the required region.");
        var intended = new HashSet<string>(spatial.Contacts.Select(t => PairKey(t.BodyAId, t.BodyBId)), StringComparer.Ordinal);
        for (int i = 0; i < spatial.Bodies.Count; i++) for (int j = i + 1; j < spatial.Bodies.Count; j++)
        {
            var a = spatial.Bodies[i]; var b = spatial.Bodies[j];
            if (a.Layer != b.Layer || intended.Contains(PairKey(a.Id, b.Id)) || !axes.TryGetValue(a.AxisId, out var aa) || !axes.TryGetValue(b.AxisId, out var ba)) continue;
            Check(GearRoutingGeometry.UnrelatedClear(new GearRoutePoint(aa.X, aa.Y), a.PitchRadius, new GearRoutePoint(ba.X, ba.Y), b.PitchRadius, g.UnrelatedClearance),
                "UNINTENDED_CONTACT", a.Id + "|" + b.Id, "Non-intended same-layer disks tangent/overlap or violate clearance: (" + aa.X + "," + aa.Y + ") / (" + ba.X + "," + ba.Y + "), layer " + a.Layer + ".");
        }
    }
    private static string PairKey(string a, string b) => StringComparer.Ordinal.Compare(a, b) < 0 ? GearRoutingContract.Pack(a, b) : GearRoutingContract.Pack(b, a);

    public static TransmissionCommonMetrics Metrics(SharedDriverTransmissionGoal g, GenerationCandidate c, IReadOnlyList<SharedDriverOutputBinding> outputs)
    {
        var axes = c.Spatial.Axes.ToDictionary(a => a.Id, StringComparer.Ordinal); var bodies = c.Spatial.Bodies;
        var endpoints = new HashSet<string>(outputs.Select(o => o.DofId).Concat(new[] { c.Kinematic.RootDofId }), StringComparer.Ordinal);
        var groups = bodies.GroupBy(b => b.DofId, StringComparer.Ordinal).ToArray();
        var compounds = groups.Where(x => x.Count() == 2 && x.Select(b => b.ToothCount).Distinct().Count() == 2).ToArray();
        var intermediates = groups.Where(x => !endpoints.Contains(x.Key)).ToArray();
        var idlers = intermediates.Count(x => x.Count() == 1); BigInteger penalty = BigInteger.Zero;
        // Branch preferences remain part of their domains; global preferred is applied once per actual intermediate shaft.
        foreach (var output in outputs)
        {
            var o = g.Outputs.Single(x => x.Key == output.OutputKey);
            foreach (var group in intermediates)
            {
                var body = group.OrderBy(b => b.Layer).First();
                if (!output.Mappings.Any(m => m.Kind == "dof" && m.MergedId == body.DofId)) continue;
                var box = group.Count() == 1 ? o.Legs[body.Layer].PreferredRegion : o.Slots[body.Layer].PreferredRegion;
                if (box != null) penalty += GearRoutingGeometry.DistanceSquared(new GearRoutePoint(axes[body.AxisId].X, axes[body.AxisId].Y), box);
            }
        }
        if (g.PreferredRegion != null) foreach (var group in intermediates) { var a = axes[group.First().AxisId]; penalty += GearRoutingGeometry.DistanceSquared(new GearRoutePoint(a.X, a.Y), g.PreferredRegion); }
        var footprint = (bodies.Max(b => axes[b.AxisId].X + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].X - b.PitchRadius)) *
            (bodies.Max(b => axes[b.AxisId].Y + b.PitchRadius) - bodies.Min(b => axes[b.AxisId].Y - b.PitchRadius));
        return new TransmissionCommonMetrics(compounds.Length, idlers, penalty, footprint, bodies.Aggregate(BigInteger.Zero, (sum, b) => sum + b.ToothCount), bodies.Select(b => b.Layer));
    }
    public static int Compare(SharedDriverTransmissionCandidate a, SharedDriverTransmissionCandidate b)
    {
        int c = a.Metrics.Compounds.CompareTo(b.Metrics.Compounds); if (c != 0) return c;
        c = a.Metrics.Idlers.CompareTo(b.Metrics.Idlers); if (c != 0) return c;
        c = a.Metrics.PreferredPenalty.CompareTo(b.Metrics.PreferredPenalty); if (c != 0) return c;
        c = a.Metrics.Footprint.CompareTo(b.Metrics.Footprint); if (c != 0) return c;
        c = a.Metrics.TotalTeeth.CompareTo(b.Metrics.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.CandidateId, b.CandidateId);
    }
}

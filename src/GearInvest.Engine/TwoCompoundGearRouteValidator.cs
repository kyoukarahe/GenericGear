using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class TwoCompoundGearRouteValidator
{
    private static string N(int n) => n.ToString("D2", CultureInfo.InvariantCulture);
    public static string Role(int leg, int index, int count) => index == 0 ? leg == 0 ? "input" : "compound:" + N(leg - 1)
        : index == count - 1 ? leg == 2 ? "output" : "compound:" + N(leg) : "leg:" + N(leg) + ":idler:" + N(index - 1);
    public static string Dof(string role) => "dof:serial-route:" + role;
    public static string Axis(string role) => "axis:serial-route:" + role;
    public static string Body(int leg, int index, int count) => "body:serial-route:" + Role(leg, index, count) +
        (Role(leg, index, count).StartsWith("compound:", StringComparison.Ordinal) ? index == 0 ? ":driving" : ":receiving" : "");
    public static string Mesh(int leg, int index) => "mesh:serial-route:leg:" + N(leg) + ":" + N(index);
    public static string Contact(int leg, int index) => "contact:" + Mesh(leg, index).Substring(5);
    internal static LowLevelMechanicalSpecification Build(IReadOnlyList<IReadOnlyList<GearRouteAssignment>> paths, BigInteger scale)
        => SerialRouteBinding.Build(paths, scale, TwoCompoundRoutingContract.Profile, Role, Dof, Axis, Body, Mesh, Contact);

    public static GearRouteMetrics Metrics(AnchoredTwoCompoundGearRoutingRequest r, IReadOnlyList<IReadOnlyList<GearRouteAssignment>> paths)
    {
        var bodies = paths.SelectMany(p => p).ToArray(); BigInteger Radius(GearRouteAssignment a) => a.Teeth * r.PitchRadiusTicksPerTooth;
        var area = (bodies.Max(a => a.Position.X + Radius(a)) - bodies.Min(a => a.Position.X - Radius(a))) *
            (bodies.Max(a => a.Position.Y + Radius(a)) - bodies.Min(a => a.Position.Y - Radius(a)));
        var preferred = BigInteger.Zero;
        for (int s = 0; s < 2; s++) if (r.Compounds[s].PreferredRegion != null) preferred += GearRoutingGeometry.DistanceSquared(paths[s + 1][0].Position, r.Compounds[s].PreferredRegion!);
        for (int leg = 0; leg < 3; leg++) if (r.Legs[leg].PreferredRegion != null)
            foreach (var a in paths[leg].Skip(1).Take(paths[leg].Count - 2)) preferred += GearRoutingGeometry.DistanceSquared(a.Position, r.Legs[leg].PreferredRegion!);
        return new GearRouteMetrics(paths.Sum(p => p.Count - 2), preferred, area, bodies.Aggregate(BigInteger.Zero, (sum, a) => sum + a.Teeth),
            GearRoutingContract.List(paths.Select(p => GearRoutingContract.List(p.Select(a => a.Canonical)))));
    }
    public static int Compare(TwoCompoundGearRouteCandidate a, TwoCompoundGearRouteCandidate b)
    {
        int c = a.Metrics.Idlers.CompareTo(b.Metrics.Idlers); if (c != 0) return c;
        c = a.Metrics.PreferredPenalty.CompareTo(b.Metrics.PreferredPenalty); if (c != 0) return c;
        c = a.Metrics.Footprint.CompareTo(b.Metrics.Footprint); if (c != 0) return c;
        c = a.Metrics.TotalTeeth.CompareTo(b.Metrics.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Metrics.Signature, b.Metrics.Signature);
    }

    public static TwoCompoundRouteValidation Validate(AnchoredTwoCompoundGearRoutingRequest raw, TwoCompoundToothAssignment assignment,
        IReadOnlyList<IReadOnlyList<GearRouteAssignment>> paths, GenerationCandidate candidate, string candidateId)
    {
        var n = AnchoredTwoCompoundGearRouter.Normalize(raw); var errors = new List<Diagnostic>();
        void Check(bool ok, string code, string subject, string detail) { if (!ok && errors.Count < GearRoutingContract.MaxDetails) errors.Add(AnchoredTwoCompoundGearRouter.Error(code, subject, detail)); }
        var generic = new GenerationEngine().Validate(candidate);
        var spatial = SpatialValidator.ValidateDetailed(candidate.Spatial, candidate.Kinematic,
            n.IsValid ? new SpatialValidationOptions(n.Request!.PitchRadiusTicksPerTooth, 3, BigInteger.Zero) : null);
        var solution = KinematicSolver.Solve(candidate.Kinematic).Solution;
        Rational? Coefficient(string role) => solution?.States.SingleOrDefault(s => s.DofId == Dof(role))?.Coefficient;
        var globals = new[] { Coefficient("input"), Coefficient("compound:00"), Coefficient("compound:01"), Coefficient("output") };
        var transfers = Enumerable.Range(0, 3).Select(i => globals[i].HasValue && globals[i] != Rational.Zero && globals[i + 1].HasValue ? globals[i + 1]!.Value / globals[i]!.Value : (Rational?)null).ToArray();
        TwoCompoundRouteValidation Finish() => new TwoCompoundRouteValidation(n.RequestId, candidateId, generic, spatial, transfers, globals[3], errors);
        if (!n.IsValid) { errors.AddRange(n.Diagnostics); return Finish(); }
        var r = n.Request!;
        Check(paths.Count == 3 && paths.All(p => p != null && p.Count >= 2 && p.Count <= 7), "PATH_LENGTH", "paths", "Exactly three ordered paths with 0..5 idlers each required.");
        if (paths.Count != 3 || paths.Any(p => p == null || p.Count < 2 || p.Count > 7)) return Finish();
        int k = paths.Sum(p => p.Count - 2); var sites = new[] { paths[1][0].Position, paths[2][0].Position };
        Check(k <= r.MaximumTotalIdlers, "TOTAL_IDLERS", "paths", "Global idler limit exceeded.");
        Check(assignment.InputTeeth == r.Input.Teeth && assignment.OutputTeeth == r.Output.Teeth, "ASSIGNMENT_BINDING", "endpoints", "Fixed endpoint teeth differ.");
        for (int s = 0; s < 2; s++)
        {
            var slot = r.Compounds[s]; var receiving = paths[s][paths[s].Count - 1]; var driving = paths[s + 1][0];
            Check(assignment.Receiving(s) != assignment.Driving(s) && slot.ReceivingTeeth.Contains(assignment.Receiving(s)) && slot.DrivingTeeth.Contains(assignment.Driving(s)) &&
                receiving.Teeth == assignment.Receiving(s) && driving.Teeth == assignment.Driving(s), "ASSIGNMENT_BINDING", "slot:" + s, "Both ordered compound tooth options must be distinct and exact.");
            Check(receiving.Position.Equals(sites[s]) && slot.Sites.Contains(sites[s]), "COMPOUND_SITE", "slot:" + s, "Paired bodies must share their permitted ordered slot.");
            Check(slot.RequiredRegion == null || GearRoutingGeometry.Contains(slot.RequiredRegion, sites[s]), "REQUIRED_SLOT_REGION", "slot:" + s, "Compound center misses required region.");
        }
        Check(paths[0][0].Position.Equals(r.Input.Position) && paths[0][0].Teeth == r.Input.Teeth && paths[2][paths[2].Count - 1].Position.Equals(r.Output.Position) && paths[2][paths[2].Count - 1].Teeth == r.Output.Teeth,
            "ANCHOR_BINDING", "endpoints", "Input/output anchors must match exactly.");
        Check(paths.SelectMany((p, leg) => leg == 0 ? p : p.Skip(1)).Select(a => a.Position).Distinct().Count() == k + 4, "GLOBAL_AXES", "paths", "Only the two intentional compounds may share axis/DOF; all other XY centers must be distinct across planes.");
        Check(candidate.Kinematic.Dofs.Count == k + 4 && candidate.Spatial.Axes.Count == k + 4 && candidate.Spatial.Bodies.Count == k + 6 && candidate.Kinematic.Couplings.Count == k + 3 && candidate.Spatial.Contacts.Count == k + 3,
            "GRAPH_COUNT", "mechanism", "Expected k+4 axes/DOFs, k+6 bodies and k+3 couplings/contacts.");
        Check(candidate.SourceId == TwoCompoundRoutingContract.Profile && candidate.Kinematic.RootDofId == Dof("input") && candidate.Solution.RootDofId == Dof("input") &&
            candidate.Kinematic.Dofs.Count(d => d.IsPrescribed) == 1 && candidate.Kinematic.Dofs.Any(d => d.Id == Dof("input") && d.IsPrescribed), "ROOT_DRIVER", "input", "Single canonical input driver/source required.");
        Check(globals[0] == Rational.One && globals[3] == r.TargetTransfer && candidate.Kinematic.Couplings.All(c => c.PhaseOffset == Rational.Zero) && solution != null && solution.States.All(s => s.PhaseOffset == Rational.Zero),
            "SIGNED_TRANSFER_PHASE", "solution", "Whole-root exact signed transfer and zero phases required, not concatenated local solutions.");
        for (int leg = 0; leg < 3; leg++)
        {
            var p = paths[leg]; var domain = r.Legs[leg]; var local = AnchoredTwoCompoundGearRouter.LocalRequest(r, assignment, sites, leg);
            Check(p.Count - 2 >= domain.MinIdlers && p.Count - 2 <= domain.MaxIdlers, "LEG_DEPTH", "leg:" + leg, "Per-leg interval violated.");
            var expectedTransfer = new Rational((p.Count % 2 == 0 ? -1 : 1) * p[0].Teeth, p[p.Count - 1].Teeth);
            Check(transfers[leg] == expectedTransfer, "LEG_TRANSFER", "leg:" + leg, "Global coefficient quotient differs from the physical leg ratio.");
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i]; var role = Role(leg, i, p.Count); var id = Body(leg, i, p.Count);
                var axis = candidate.Spatial.Axes.FirstOrDefault(x => x.Id == Axis(role)); var body = candidate.Spatial.Bodies.FirstOrDefault(x => x.Id == id);
                Check(candidate.Kinematic.Dofs.Any(d => d.Id == Dof(role)) && axis != null && axis.X == a.Position.X && axis.Y == a.Position.Y, "AXIS_DOF_BINDING", id, "Exact role axis/DOF binding differs.");
                Check(body != null && body.Kind == SpatialBodyKind.Gear && body.AxisId == Axis(role) && body.DofId == Dof(role) && body.Layer == leg && body.ToothCount == a.Teeth && body.PitchRadius == a.Teeth * r.PitchRadiusTicksPerTooth && body.ExactMountingPhase == Rational.Zero,
                    "BODY_BINDING", id, "Role/body/layer/tooth/radius/mounting binding differs.");
                if (i > 0 && i < p.Count - 1) Check(domain.Sites.Contains(a.Position) && domain.IdlerTeeth.Contains(a.Teeth), "DOMAIN_MEMBERSHIP", id, "Idler outside declared finite options.");
                var issue = GearRouteValidator.StaticIssue(local, a); Check(issue == null, "ENVIRONMENT", id, "Pitch disk violates plane bounds/keep-out: " + issue);
                for (int j = 0; j < i - 1; j++) Check(GearRoutingGeometry.UnrelatedClear(a.Position, GearRouteValidator.Radius(local, a), p[j].Position, GearRouteValidator.Radius(local, p[j]), domain.UnrelatedClearance), "UNINTENDED_CONTACT", id, "Nonconsecutive disks overlap/touch or violate clearance.");
                if (i == p.Count - 1) continue;
                var c = candidate.Kinematic.Couplings.FirstOrDefault(x => x.Id == Mesh(leg, i)); var contact = candidate.Spatial.Contacts.FirstOrDefault(x => x.Id == Contact(leg, i));
                Check(c != null && c.DriverDofId == Dof(role) && c.DrivenDofId == Dof(Role(leg, i + 1, p.Count)) && c.DriverTeeth == a.Teeth && c.DrivenTeeth == p[i + 1].Teeth, "COUPLING_BINDING", Mesh(leg, i), "Only consecutive same-leg external couplings permitted.");
                Check(contact != null && contact.Kind == SpatialContactKind.ExternalGearMesh && contact.ConstraintId == Mesh(leg, i) && contact.BodyAId == id && contact.BodyBId == Body(leg, i + 1, p.Count), "CONTACT_BINDING", Contact(leg, i), "Contact binding differs; compound bodies must not mesh.");
            }
            foreach (var region in domain.RequiredRegions) Check(p.Skip(1).Take(p.Count - 2).Any(a => GearRoutingGeometry.Contains(region.Bounds, a.Position)), "REQUIRED_REGION", region.Id, "No intermediate visits required region.");
        }
        return Finish();
    }
}

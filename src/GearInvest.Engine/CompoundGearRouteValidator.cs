using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class CompoundGearRouteValidator
{
    public static string Role(int leg, int index, int count) => leg == 0
        ? index == 0 ? "input" : index == count - 1 ? "shared" : "left-idler:" + (index - 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
        : index == 0 ? "shared" : index == count - 1 ? "output" : "right-idler:" + (index - 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public static string Dof(string role) => "dof:compound-route:" + role;
    public static string Axis(string role) => "axis:compound-route:" + role;
    public static string Body(int leg, int index, int count) => "body:compound-route:" +
        (Role(leg, index, count) == "shared" ? leg == 0 ? "receiving" : "driving" : Role(leg, index, count));
    public static string Mesh(int leg, int index) => "mesh:compound-route:" + (leg == 0 ? "left:" : "right:") + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public static string Contact(int leg, int index) => "contact:" + Mesh(leg, index).Substring(5);

    internal static LowLevelMechanicalSpecification Build(IReadOnlyList<GearRouteAssignment> left, IReadOnlyList<GearRouteAssignment> right, BigInteger scale)
        => SerialRouteBinding.Build(new[] { left, right }, scale, CompoundRoutingContract.Profile, Role, Dof, Axis, Body, Mesh, Contact);

    public static GearRouteMetrics Metrics(AnchoredCompoundGearRoutingRequest r, IReadOnlyList<GearRouteAssignment> left, IReadOnlyList<GearRouteAssignment> right)
    {
        var bodies = left.Concat(right).ToArray(); BigInteger Radius(GearRouteAssignment a) => a.Teeth * r.PitchRadiusTicksPerTooth;
        var area = (bodies.Max(a => a.Position.X + Radius(a)) - bodies.Min(a => a.Position.X - Radius(a))) *
            (bodies.Max(a => a.Position.Y + Radius(a)) - bodies.Min(a => a.Position.Y - Radius(a)));
        var preferred = BigInteger.Zero;
        if (r.PreferredCompoundRegion != null) preferred += GearRoutingGeometry.DistanceSquared(left[left.Count - 1].Position, r.PreferredCompoundRegion);
        for (int leg = 0; leg < 2; leg++)
        {
            var p = leg == 0 ? left : right; var box = leg == 0 ? r.InputLeg.PreferredRegion : r.OutputLeg.PreferredRegion;
            if (box != null) foreach (var a in p.Skip(1).Take(p.Count - 2)) preferred += GearRoutingGeometry.DistanceSquared(a.Position, box);
        }
        return new GearRouteMetrics(left.Count + right.Count - 4, preferred, area, bodies.Aggregate(BigInteger.Zero, (sum, a) => sum + a.Teeth),
            GearRoutingContract.Pack(GearRoutingContract.List(left.Select(a => a.Canonical)), GearRoutingContract.List(right.Select(a => a.Canonical))));
    }
    public static int Compare(CompoundGearRouteCandidate a, CompoundGearRouteCandidate b)
    {
        int c = a.Metrics.Idlers.CompareTo(b.Metrics.Idlers); if (c != 0) return c;
        c = a.Metrics.PreferredPenalty.CompareTo(b.Metrics.PreferredPenalty); if (c != 0) return c;
        c = a.Metrics.Footprint.CompareTo(b.Metrics.Footprint); if (c != 0) return c;
        c = a.Metrics.TotalTeeth.CompareTo(b.Metrics.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Metrics.Signature, b.Metrics.Signature);
    }

    public static CompoundRouteValidation Validate(AnchoredCompoundGearRoutingRequest raw, AnchoredCompoundToothPair pair,
        IReadOnlyList<GearRouteAssignment> left, IReadOnlyList<GearRouteAssignment> right, GenerationCandidate candidate, string candidateId)
    {
        var n = AnchoredCompoundGearRouter.Normalize(raw); var errors = new List<Diagnostic>();
        void Check(bool ok, string code, string subject, string detail) { if (!ok && errors.Count < GearRoutingContract.MaxDetails) errors.Add(AnchoredCompoundGearRouter.Error(code, subject, detail)); }
        var generic = new GenerationEngine().Validate(candidate);
        var spatial = SpatialValidator.ValidateDetailed(candidate.Spatial, candidate.Kinematic,
            n.IsValid ? new SpatialValidationOptions(n.Request!.PitchRadiusTicksPerTooth, 2, BigInteger.Zero) : null);
        var solution = KinematicSolver.Solve(candidate.Kinematic).Solution;
        var shared = solution?.States.SingleOrDefault(s => s.DofId == Dof("shared")); var output = solution?.States.SingleOrDefault(s => s.DofId == Dof("output"));
        var q0 = shared?.Coefficient; var q = output?.Coefficient; Rational? q1 = q0.HasValue && q0.Value != Rational.Zero && q.HasValue ? q.Value / q0.Value : (Rational?)null;
        CompoundRouteValidation Finish() => new CompoundRouteValidation(n.RequestId, candidateId, generic, spatial, q0, q1, q, errors);
        if (!n.IsValid) { errors.AddRange(n.Diagnostics); return Finish(); }
        var r = n.Request!;
        Check(left.Count >= 2 && left.Count <= 7 && right.Count >= 2 && right.Count <= 7, "PATH_LENGTH", "paths", "Each leg requires 0..5 idlers.");
        if (left.Count < 2 || left.Count > 7 || right.Count < 2 || right.Count > 7) return Finish();
        int axes = left.Count + right.Count - 1, bodies = axes + 1, contacts = axes - 1;
        Check(left.Count + right.Count - 4 <= r.MaximumTotalIdlers, "TOTAL_IDLERS", "paths", "Combined idler count exceeds global limit.");
        Check(pair.InputTeeth == r.Input.Teeth && pair.OutputTeeth == r.Output.Teeth && pair.ReceivingTeeth != pair.DrivingTeeth && r.ReceivingTeeth.Contains(pair.ReceivingTeeth) && r.DrivingTeeth.Contains(pair.DrivingTeeth),
            "PAIR_BINDING", "pair", "Endpoint-conditioned distinct compound tooth options required.");
        var site = left[left.Count - 1].Position;
        Check(r.CompoundSites.Contains(site) && right[0].Position.Equals(site), "COMPOUND_SITE", "shared", "Both bodies require the same permitted compound point.");
        Check(r.RequiredCompoundRegion == null || GearRoutingGeometry.Contains(r.RequiredCompoundRegion, site), "REQUIRED_COMPOUND_REGION", "shared", "Required compound-center region not satisfied.");
        Check(left[0].Position.Equals(r.Input.Position) && left[0].Teeth == r.Input.Teeth && right[right.Count - 1].Position.Equals(r.Output.Position) && right[right.Count - 1].Teeth == r.Output.Teeth &&
            left[left.Count - 1].Teeth == pair.ReceivingTeeth && right[0].Teeth == pair.DrivingTeeth, "ANCHOR_TEETH", "anchors", "Fixed endpoints and selected compound teeth must be exact.");
        Check(left.Concat(right.Skip(1)).Select(a => a.Position).Distinct().Count() == axes, "GLOBAL_AXES", "paths", "Distinct DOF axes cannot coincide, including across layers.");
        Check(candidate.Kinematic.Dofs.Count == axes && candidate.Spatial.Axes.Count == axes && candidate.Spatial.Bodies.Count == bodies && candidate.Kinematic.Couplings.Count == contacts && candidate.Spatial.Contacts.Count == contacts,
            "GRAPH_COUNT", "mechanism", "Exactly k+3 axes/DOFs, k+4 bodies and k+2 contacts/couplings required.");
        Check(candidate.Kinematic.RootDofId == Dof("input") && candidate.Solution.RootDofId == Dof("input") && candidate.Kinematic.Dofs.Count(d => d.IsPrescribed) == 1 && candidate.Kinematic.Dofs.Any(d => d.Id == Dof("input") && d.IsPrescribed), "ROOT_DRIVER", "input", "One canonical input driver required.");
        Check(q == r.TargetTransfer && q0.HasValue && q1.HasValue && candidate.Kinematic.Couplings.All(c => c.PhaseOffset == Rational.Zero) && solution != null && solution.States.All(s => s.PhaseOffset == Rational.Zero),
            "SIGNED_TRANSFER_PHASE", "solution", "Whole-graph solved transfer and all zero phases must match; local-root solutions are not authoritative.");
        for (int leg = 0; leg < 2; leg++)
        {
            var path = leg == 0 ? left : right; var domain = leg == 0 ? r.InputLeg : r.OutputLeg;
            var local = AnchoredCompoundGearRouter.LocalRequest(r, pair, site, leg, 1);
            Check(path.Count - 2 >= domain.MinIdlers && path.Count - 2 <= domain.MaxIdlers, "LEG_DEPTH", "leg:" + leg, "Per-leg idler interval required.");
            for (int i = 0; i < path.Count; i++)
            {
                var assignment = path[i]; var role = Role(leg, i, path.Count); var id = Body(leg, i, path.Count);
                var axis = candidate.Spatial.Axes.FirstOrDefault(a => a.Id == Axis(role)); var body = candidate.Spatial.Bodies.FirstOrDefault(b => b.Id == id);
                Check(candidate.Kinematic.Dofs.Any(d => d.Id == Dof(role)) && axis != null && axis.X == assignment.Position.X && axis.Y == assignment.Position.Y, "AXIS_DOF_BINDING", id, "Canonical exact axis/DOF role differs.");
                Check(body != null && body.Kind == SpatialBodyKind.Gear && body.AxisId == Axis(role) && body.DofId == Dof(role) && body.Layer == leg && body.ToothCount == assignment.Teeth && body.PitchRadius == assignment.Teeth * r.PitchRadiusTicksPerTooth && body.ExactMountingPhase == Rational.Zero,
                    "BODY_BINDING", id, "Canonical body/axis/DOF/layer/teeth/scale/phase differs.");
                if (i > 0 && i < path.Count - 1) Check(domain.Sites.Contains(assignment.Position) && domain.IdlerTeeth.Contains(assignment.Teeth), "DOMAIN_MEMBERSHIP", id, "Idler lies outside its finite leg options.");
                var issue = GearRouteValidator.StaticIssue(local, assignment); Check(issue == null, "ENVIRONMENT", id, "Whole pitch disk violates its plane bounds/keep-out: " + issue);
                for (int j = 0; j < i - 1; j++) Check(GearRoutingGeometry.UnrelatedClear(assignment.Position, GearRouteValidator.Radius(local, assignment), path[j].Position, GearRouteValidator.Radius(local, path[j]), domain.UnrelatedClearance),
                    "UNINTENDED_CONTACT", id, "Nonconsecutive same-plane disks overlap, are tangent or violate clearance.");
                if (i == path.Count - 1) continue;
                var c = candidate.Kinematic.Couplings.FirstOrDefault(x => x.Id == Mesh(leg, i)); var contact = candidate.Spatial.Contacts.FirstOrDefault(x => x.Id == Contact(leg, i));
                Check(c != null && c.DriverDofId == Dof(role) && c.DrivenDofId == Dof(Role(leg, i + 1, path.Count)) && c.DriverTeeth == assignment.Teeth && c.DrivenTeeth == path[i + 1].Teeth,
                    "COUPLING_BINDING", Mesh(leg, i), "Each consecutive external coupling must match its path.");
                Check(contact != null && contact.Kind == SpatialContactKind.ExternalGearMesh && contact.ConstraintId == Mesh(leg, i) && contact.BodyAId == id && contact.BodyBId == Body(leg, i + 1, path.Count),
                    "CONTACT_BINDING", Contact(leg, i), "Only same-leg consecutive contacts allowed; no mesh between compound bodies.");
            }
            foreach (var region in domain.RequiredRegions) Check(path.Skip(1).Take(path.Count - 2).Any(a => GearRoutingGeometry.Contains(region.Bounds, a.Position)), "REQUIRED_REGION", region.Id, "No intermediate center visits required region.");
        }
        return Finish();
    }
}

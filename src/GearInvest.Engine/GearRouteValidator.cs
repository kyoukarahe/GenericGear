using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class GearRouteValidator
{
    public static string Role(int index, int count) => index == 0 ? "input" : index == count - 1 ? "output" : "idler:" + (index - 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public static string Dof(int index, int count) => "dof:route:" + Role(index, count);
    public static string Axis(int index, int count) => "axis:route:" + Role(index, count);
    public static string Body(int index, int count) => "body:route:" + Role(index, count);
    public static string Mesh(int index) => "mesh:route:" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public static string Contact(int index) => "contact:route:" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public static BigInteger Radius(AnchoredGearRoutingRequest r, GearRouteAssignment a) => r.PitchRadiusTicksPerTooth * a.Teeth;
    public static string? StaticIssue(AnchoredGearRoutingRequest r, GearRouteAssignment a)
    {
        var radius = Radius(r, a);
        if (!GearRoutingGeometry.ContainsDisk(r.Bounds, a.Position, radius)) return "BOUNDARY";
        foreach (var box in r.KeepOuts) if (!GearRoutingGeometry.ClearsKeepOut(a.Position, radius, box.Bounds, r.KeepOutClearance)) return "KEEPOUT:" + box.Id;
        return null;
    }
    public static GearRouteMetrics Metrics(AnchoredGearRoutingRequest r, IReadOnlyList<GearRouteAssignment> path)
    {
        var area = (path.Max(a => a.Position.X + Radius(r, a)) - path.Min(a => a.Position.X - Radius(r, a))) *
                   (path.Max(a => a.Position.Y + Radius(r, a)) - path.Min(a => a.Position.Y - Radius(r, a)));
        var penalty = BigInteger.Zero;
        if (r.PreferredRegion != null) foreach (var a in path.Skip(1).Take(path.Count - 2)) penalty += GearRoutingGeometry.DistanceSquared(a.Position, r.PreferredRegion);
        return new GearRouteMetrics(path.Count - 2, penalty, area, path.Aggregate(BigInteger.Zero, (sum, a) => sum + a.Teeth), GearRoutingContract.List(path.Select(a => a.Canonical)));
    }
    public static int Compare(GearRouteCandidate a, GearRouteCandidate b)
    {
        int c = a.Metrics.Idlers.CompareTo(b.Metrics.Idlers); if (c != 0) return c;
        c = a.Metrics.PreferredPenalty.CompareTo(b.Metrics.PreferredPenalty); if (c != 0) return c;
        c = a.Metrics.Footprint.CompareTo(b.Metrics.Footprint); if (c != 0) return c;
        c = a.Metrics.TotalTeeth.CompareTo(b.Metrics.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Metrics.Signature, b.Metrics.Signature);
    }

    public static GearRouteValidation Validate(AnchoredGearRoutingRequest raw, IReadOnlyList<GearRouteAssignment> path, GenerationCandidate candidate, string candidateId)
    {
        var normalized = AnchoredGearRouter.Normalize(raw); var errors = new List<Diagnostic>();
        void Check(bool ok, string code, string subject, string detail) { if (!ok && errors.Count < GearRoutingContract.MaxDetails) errors.Add(AnchoredGearRouter.Error(code, subject, detail)); }
        var generic = new GenerationEngine().Validate(candidate);
        var spatial = SpatialValidator.ValidateDetailed(candidate.Spatial, candidate.Kinematic,
            normalized.IsValid ? new SpatialValidationOptions(normalized.Request!.PitchRadiusTicksPerTooth, 1, normalized.Request.UnrelatedClearance) : null);
        if (!normalized.IsValid) return new GearRouteValidation(normalized.RequestId, candidateId, generic, spatial, null, normalized.Diagnostics);
        var r = normalized.Request!; int n = path.Count;
        Check(n >= 2 && n <= 7 && n - 2 >= r.MinIdlers && n - 2 <= r.MaxIdlerCount, "PATH_LENGTH", "path", "Path must obey the 0..5 idler interval.");
        var solved = KinematicSolver.Solve(candidate.Kinematic);
        var actual = solved.Solution?.States.SingleOrDefault(s => s.DofId == "dof:route:output");
        if (n < 2 || n > 7) return new GearRouteValidation(normalized.RequestId, candidateId, generic, spatial, actual?.Coefficient, errors);
        Check(candidate.Kinematic.RootDofId == Dof(0, n) && candidate.Solution.RootDofId == Dof(0, n), "ROOT", "input", "Input must be the canonical root.");
        Check(candidate.Kinematic.Dofs.Count == n && candidate.Spatial.Axes.Count == n && candidate.Spatial.Bodies.Count == n &&
              candidate.Kinematic.Couplings.Count == n - 1 && candidate.Spatial.Contacts.Count == n - 1, "GRAPH_COUNT", "mechanism", "One DOF/axis/body per gear; one coupling/contact per consecutive pair.");
        Check(candidate.Kinematic.Dofs.Count(d => d.IsPrescribed) == 1 && candidate.Kinematic.Dofs.Any(d => d.Id == Dof(0, n) && d.IsPrescribed), "DRIVER", "input", "Exactly one input driver is required.");
        Check(path[0].Position.Equals(r.Input.Position) && path[0].Teeth == r.Input.Teeth && path[n - 1].Position.Equals(r.Output.Position) && path[n - 1].Teeth == r.Output.Teeth,
            "ENDPOINT", "anchors", "Endpoints and endpoint teeth must be unchanged.");
        Check(path.Select(a => a.Position).Distinct().Count() == n, "OCCUPIED_POSITION", "path", "An axis position may occur only once.");
        Check(actual != null && actual.Coefficient == r.TargetTransfer && actual.PhaseOffset == Rational.Zero && candidate.Kinematic.Couplings.All(c => c.PhaseOffset == Rational.Zero),
            "SIGNED_TRANSFER", "output", "Actual solved output transfer and every phase must satisfy the signed zero-phase contract.");
        var axisMap = candidate.Spatial.Axes.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var bodyMap = candidate.Spatial.Bodies.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var a = path[i]; var id = Body(i, n);
            Check(a.Teeth > 0 && a.Teeth <= GearRoutingContract.MaxTeeth, "TEETH", id, "Positive bounded tooth count required.");
            if (i > 0 && i < n - 1) Check(r.Sites.Contains(a.Position) && r.IdlerTeeth.Contains(a.Teeth), "DOMAIN_MEMBERSHIP", id, "Intermediate position/tooth assignment is outside the normalized domain.");
            Check(axisMap.TryGetValue(Axis(i, n), out var axis) && axis.X == a.Position.X && axis.Y == a.Position.Y, "AXIS_BINDING", id, "Exact path-to-axis binding mismatch.");
            Check(bodyMap.TryGetValue(id, out var body) && body.Kind == SpatialBodyKind.Gear && body.AxisId == Axis(i, n) && body.DofId == Dof(i, n) && body.Layer == 0 &&
                body.ToothCount == a.Teeth && body.PitchRadius == Radius(r, a) && body.ExactMountingPhase == Rational.Zero, "BODY_BINDING", id, "One canonical shared gear body, scale, DOF and zero mounting phase required.");
            var issue = StaticIssue(r, a); Check(issue == null, issue ?? "ENVIRONMENT", id, "Whole pitch disk violates routing bounds or closed keep-out clearance.");
            for (int j = 0; j < i - 1; j++) Check(GearRoutingGeometry.UnrelatedClear(a.Position, Radius(r, a), path[j].Position, Radius(r, path[j]), r.UnrelatedClearance),
                "UNINTENDED_CONTACT", Body(j, n) + "|" + id, "Nonconsecutive pitch disks overlap, are tangent or violate unrelated clearance.");
            if (i == n - 1) continue;
            var c = candidate.Kinematic.Couplings.FirstOrDefault(x => x.Id == Mesh(i)); var contact = candidate.Spatial.Contacts.FirstOrDefault(x => x.Id == Contact(i));
            Check(c != null && c.DriverDofId == Dof(i, n) && c.DrivenDofId == Dof(i + 1, n) && c.DriverTeeth == a.Teeth && c.DrivenTeeth == path[i + 1].Teeth,
                "COUPLING_BINDING", Mesh(i), "Consecutive simple-chain coupling mismatch.");
            Check(contact != null && contact.Kind == SpatialContactKind.ExternalGearMesh && contact.ConstraintId == Mesh(i) && contact.BodyAId == id && contact.BodyBId == Body(i + 1, n),
                "CONTACT_BINDING", Contact(i), "Consecutive meshes must reuse the same canonical intermediate body.");
        }
        foreach (var region in r.RequiredRegions) Check(path.Skip(1).Take(n - 2).Any(a => GearRoutingGeometry.Contains(region.Bounds, a.Position)), "REQUIRED_REGION", region.Id, "No intermediate center visits the required region.");
        return new GearRouteValidation(normalized.RequestId, candidateId, generic, spatial, actual?.Coefficient, errors);
    }
}

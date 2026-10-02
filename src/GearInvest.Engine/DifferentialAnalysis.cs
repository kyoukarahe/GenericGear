using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class DifferentialAnalyzer
{
    public static DifferentialAnalysis Prepare(DifferentialRequest request) => Analyze(request, null);
    public static DifferentialAnalysis AnalyzeBoundary(DifferentialDefinition definition, IEnumerable<DifferentialBoundary> boundary) =>
        Analyze(new DifferentialRequest(definition, Array.Empty<string>()), DifferentialProfile.Set(boundary, x => x.Id, 6));

    private static DifferentialAnalysis Analyze(DifferentialRequest request, IReadOnlyCollection<DifferentialBoundary>? boundary)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var d = request.Definition; bool numeric = boundary is not null;
        var conditions = boundary ?? Array.Empty<DifferentialBoundary>();
        var checks = new List<OrientedDomainCheck>(); var diagnostics = new List<MechanicalDiagnostic>();
        var ids = new[] { d.CarrierShaft.Id, d.SunShaft.Id, d.PlanetShaft.Id }.Concat(d.Prefix?.Definition.Shafts.Where(s => s.Id != d.CarrierShaft.Id).Select(s => s.Id) ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        bool Check(string domain, bool ok, string detail)
        {
            checks.Add(new(domain, d.DefinitionId, ok ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (!ok) diagnostics.Add(new("Differential" + domain, "DifferentialAdmission", detail: detail)); return ok;
        }
        DifferentialAnalysis Refuse() => new(request, numeric, conditions, checks, diagnostics, MechanicalDeterminacy.BlockedByInvalidConstraint, ids);
        foreach (var domain in new[] { "ToothSolids", "SweptSolids", "Dynamics" }.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            if (!new[] { "Ownership", "UnitsAndFrames", "ContactAndReference", "SourceMechanics", "PitchPlanes" }.Contains(domain))
                checks.Add(new(domain, d.DefinitionId, OrientedCheckVerdict.NotPerformed, d.RequiredValidationDomains.Contains(domain), "Outside bounded ideal contact/pitch-plane certification."));
        var source = d.Prefix?.Definition; var map = d.PrefixMapping; MechanicalAnalysis? sourceAnalysis = null;
        bool prefixShape = source is null ? map is null : map is not null && source.Shafts.Count == 2 && source.Bodies.Count == 2 && source.Contacts.Count == 1 &&
            source.Contacts[0].Kind == OrientedContactKind.ExternalSpur && source.CoaxialLayout is null && source.Connections.Count == 0 && source.KeepOuts.Count == 0 &&
            source.Ports.Count <= 4 && source.Outputs.Count <= 4 && source.Bodies.Select(b => b.ShaftId).Distinct(StringComparer.Ordinal).Count() == 2 &&
            source.RootShaftId != d.CarrierShaft.Id && source.Shafts.Count(s => s.IsPrescribed) == 1 && source.Shafts.Any(s => s.Id == source.RootShaftId && s.IsPrescribed) &&
            source.Shafts.Any(s => s.Id == d.CarrierShaft.Id && !s.IsPrescribed && OrientedGoalKeys.Frame(map.FrameMm(s.Frame)) == OrientedGoalKeys.Frame(d.CarrierShaft.Frame));
        var bodyIds = (source?.Bodies.Select(b => b.Id) ?? Array.Empty<string>()).Concat(new[] { d.CarrierBodyId, d.SunBodyId, d.PlanetBodyId }).ToArray();
        if (!Check("Ownership", prefixShape && ids.Length <= 4 && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length &&
            bodyIds.Distinct(StringComparer.Ordinal).Count() == bodyIds.Length && !d.CarrierShaft.IsPrescribed && !d.SunShaft.IsPrescribed && !d.PlanetShaft.IsPrescribed &&
            d.PlanetShaft.CarrierShaftId == d.CarrierShaft.Id && d.Ports.All(p => ids.Contains(p.ShaftId)) && d.Holds.All(h => ids.Contains(h.ShaftId)) &&
            request.InputPortIds.All(p => d.Ports.Any(x => x.Id == p)) && conditions.All(c => d.Ports.Any(p => p.Id == c.PortId)),
            "Three distinct physical coordinates, explicit owners/ports/holds; optional actual one-stage prefix with unchanged prescribed source root.")) return Refuse();
        if (d.Prefix is not null) sourceAnalysis = MechanicalAnalyzer.Analyze(d.Prefix);
        if (!Check("SourceMechanics", sourceAnalysis is null || sourceAnalysis.IsMechanicallyValid,
            "Current actual prefix analysis; source provenance and export admission additionally checked by facade finalization.")) return Refuse();
        var plane = d.PlaneMm; var carrier = d.CarrierShaft.Frame; var sun = d.SunShaft.Frame; var local = d.PlanetShaft.FrameInCarrier;
        bool frames = new[] { plane, carrier, sun, local }.All(f => f.IsProperCardinal) && carrier.Z.Cross(plane.Z) == default &&
            (plane.Origin - carrier.Origin).Cross(carrier.Z) == default && sun.Z.Cross(plane.Z) == default && sun.Origin == plane.Origin && local.Z.Cross(ExactVector3.UnitZ) == default &&
            d.Ports.All(p => p.FrameInShaft.IsProperCardinal && p.FrameInShaft.Z.Cross(ExactVector3.UnitZ) == default && p.FrameInShaft.Origin.X == 0 && p.FrameInShaft.Origin.Y == 0) &&
            (map is null || map.MillimetersPerSourceUnit > 0 && map.PoseMm.IsProperCardinal && source!.Shafts.All(s => map.Direction(s.Frame.Z).Cross(plane.Z) == default));
        bool units = d.Module.Kind == QuantityKind.LinearPosition && d.Module.Value > 0 &&
            new[] { d.CarrierReference, d.SunReference, d.PlanetReference, d.SunMount, d.PlanetMount }.Concat(d.Ports.Select(p => p.ReadoutOffset)).All(q => q.Kind == QuantityKind.AngularPosition);
        if (!Check("UnitsAndFrames", frames && units, "Signed proper parallel frames, positive mm module/mapping, shaft-local ports and unwrapped turns.")) return Refuse();
        var ec = plane.Z.Dot(carrier.Z); var gc = CarrierAnalyzer.Quarter(plane.X.Dot(carrier.X), plane.Y.Dot(carrier.X));
        var es = plane.Z.Dot(sun.Z); var gs = CarrierAnalyzer.Quarter(plane.X.Dot(sun.X), plane.Y.Dot(sun.X));
        var ep = local.Z.Z; var gp = CarrierAnalyzer.Quarter(local.X.X, local.X.Y);
        var c0 = gc + ec * d.CarrierReference.Value; var s0 = gs + es * (d.SunReference.Value + d.SunMount.Value); var p0 = gp + ep * (d.PlanetReference.Value + d.PlanetMount.Value);
        var rs = d.Module.Value * d.SunTeeth / 2; var rp = d.Module.Value * d.PlanetTeeth / 2;
        bool contact = d.ContactPresent && d.SunTeeth >= 1 && d.SunTeeth <= 4096 && d.PlanetTeeth >= 1 && d.PlanetTeeth <= 4096 && d.ToothRegistration.Denominator == 1 &&
            local.Origin == new ExactVector3(rs + rp, 0, 0) && new CarrierMeshConstraint(d.SunTeeth, d.PlanetTeeth, d.ToothRegistration).Residual(s0, c0, p0) == 0;
        if (!Check("ContactAndReference", contact, "Actual external pitch tangency and immutable integer registration at the authored signed reference.")) return Refuse();
        if (!Check("PitchPlanes", source is null || source.Bodies.All(b => (map!.PointMm(b.MountingFrame.Origin) - plane.Origin).Dot(plane.Z) != 0),
            "Moving pitch disks tangent for all phase; actual prefix pitch planes separated. Arm/shaft/teeth/solids not certified.")) return Refuse();
        var rows = new List<ExactLinearRow>(); int rhsCount = 1 + request.InputPortIds.Count;
        void Row(string id, IEnumerable<KeyValuePair<string, Rational>> coefficients, Rational offset, string? input = null)
        {
            var a = new Rational[ids.Length]; foreach (var p in coefficients) a[Array.IndexOf(ids, p.Key)] += p.Value;
            var b = new Rational[rhsCount]; b[0] = offset;
            if (input is not null) b[1 + request.InputPortIds.IndexOf(input)] = 1;
            rows.Add(new(id, a, b));
        }
        KeyValuePair<string, Rational> Term(string id, Rational q) => new(id, q);
        Row("contact/external-carrier", new[] { Term(d.SunShaft.Id, d.SunTeeth * es), Term(d.PlanetShaft.Id, d.PlanetTeeth * ep), Term(d.CarrierShaft.Id, -(d.SunTeeth + d.PlanetTeeth) * ec) },
            d.ToothRegistration - d.SunTeeth * (gs + es * d.SunMount.Value) - d.PlanetTeeth * (gp + ep * d.PlanetMount.Value) + (d.SunTeeth + d.PlanetTeeth) * gc);
        if (sourceAnalysis is not null)
        {
            var relations = sourceAnalysis.AdmittedComponents.SelectMany(c => c.Affine!.Relations).ToDictionary(r => r.DofId, r => r.Relation, StringComparer.Ordinal);
            if (!relations.TryGetValue(d.CarrierShaft.Id, out var law) || law.Coefficient == 0) { Check("SourceMechanics", false, "Actual carrier source output must be driven."); return Refuse(); }
            Row("prefix/" + source!.Contacts[0].Id, new[] { Term(d.CarrierShaft.Id, 1), Term(source.RootShaftId!, -law.Coefficient) }, law.Phase);
        }
        foreach (var h in d.Holds) Row("hold/" + h.Id, new[] { Term(h.ShaftId, 1) }, h.Position.Value);
        foreach (var id in request.InputPortIds)
        { var port = d.Ports.Single(p => p.Id == id); Row("input/" + id, new[] { Term(port.ShaftId, port.FrameInShaft.Z.Z) }, -port.ReadoutOffset.Value, id); }
        foreach (var condition in conditions)
        { var port = d.Ports.Single(p => p.Id == condition.PortId); Row("boundary/" + condition.Id, new[] { Term(port.ShaftId, port.FrameInShaft.Z.Z) }, condition.Position.Value - port.ReadoutOffset.Value); }
        var reduced = BoundedLinearBlock.Reduce(ids.Length, rhsCount, rows);
        if (reduced.HasIncompatibleRightHandSide)
        {
            var witnesses = reduced.Rows.Where(r => r.Pivot < 0 && r.RightHandSide.Any(v => v != 0)).SelectMany(r => r.WitnessRows).Distinct(StringComparer.Ordinal).ToArray();
            diagnostics.Add(new("DifferentialInconsistentRows", "ExactElimination", related: witnesses.Select(id => new MechanicalReference("ConstraintRow", id)),
                detail: numeric ? "Inconsistent explicit numeric conditions; no arbitrary subset selected." : "Selected ports/holds do not permit an arbitrary independent vector; inspect numeric boundary analysis for consistent redundancy."));
            bool constantConflict = reduced.Rows.Any(r => r.Pivot < 0 && r.RightHandSide[0] != 0 && r.RightHandSide.Skip(1).All(v => v == 0));
            return new(request, numeric, conditions, checks, diagnostics, numeric || constantConflict ? MechanicalDeterminacy.InconsistentConstraints : MechanicalDeterminacy.InconsistentWithPrescribedInput, ids, rows, reduced);
        }
        DifferentialLaw Law(IEnumerable<Rational> rhs) { var a = rhs.ToArray(); return new(request.InputPortIds.Select((id, i) => Term(id, a[i + 1])), a[0]); }
        DifferentialLaw Constant(Rational v) => new(request.InputPortIds.Select(id => Term(id, 0)), v);
        var coordinates = ids.Select((id, col) =>
        {
            var row = reduced.Rows.SingleOrDefault(r => r.Pivot == col);
            return row is null ? new DifferentialCoordinate(id, Constant(0), new[] { Term(id, 1) }) :
                new DifferentialCoordinate(id, Law(row.RightHandSide), reduced.FreeColumns.Where(c => row.Coefficients[c] != 0).Select(c => Term(ids[c], -row.Coefficients[c])));
        }).ToArray();
        var status = coordinates.All(c => c.IsKnown) ? (conditions.Count > 0 || request.InputPortIds.Count > 0 ? MechanicalDeterminacy.DeterminedBySelectedInput : MechanicalDeterminacy.PinnedByConstraints) : MechanicalDeterminacy.UndrivenRelativeMotion;
        if (!coordinates.All(c => c.IsKnown)) return new(request, numeric, conditions, checks, diagnostics, status, ids, rows, reduced, coordinates);
        var laws = coordinates.ToDictionary(c => c.ShaftId, c => c.Law!, StringComparer.Ordinal);
        var commonC = laws[d.CarrierShaft.Id].Then(ec, gc); var commonP = laws[d.PlanetShaft.Id].Then(ep, gp + ep * d.PlanetMount.Value);
        var relative = laws[d.PlanetShaft.Id].Minus(commonC.Then(ep, 0)); var poses = new List<DifferentialPoseNode>();
        if (source is not null)
        {
            foreach (var s in source.Shafts) poses.Add(new("source-shaft/" + s.Id, null, map!.FrameMm(s.Frame), laws[s.Id], s.Id));
            foreach (var b in source.Bodies) poses.Add(new("source-body/" + b.Id, "source-shaft/" + b.ShaftId,
                CarrierAnalyzer.InFrame(map!.FrameMm(source.Shafts.Single(s => s.Id == b.ShaftId).Frame), map.FrameMm(b.MountingFrame)), Constant(0), b.ShaftId, b.Id, b.Teeth, map.MillimetersPerSourceUnit * b.OuterPitchRadius));
        }
        else poses.Add(new("carrier-shaft", null, carrier, laws[d.CarrierShaft.Id], d.CarrierShaft.Id));
        poses.Add(new("carrier-frame", null, plane, commonC, d.CarrierShaft.Id));
        poses.Add(new("sun-shaft", null, sun, laws[d.SunShaft.Id], d.SunShaft.Id));
        poses.Add(new("planet-shaft", "carrier-frame", local, relative, d.PlanetShaft.Id));
        poses.Add(new("carrier-body", "carrier-frame", OrientedFrame.Identity, Constant(0), d.CarrierShaft.Id, d.CarrierBodyId));
        poses.Add(new("sun-body", "sun-shaft", OrientedFrame.Identity, Constant(d.SunMount.Value), d.SunShaft.Id, d.SunBodyId, d.SunTeeth, rs));
        poses.Add(new("planet-body", "planet-shaft", OrientedFrame.Identity, Constant(d.PlanetMount.Value), d.PlanetShaft.Id, d.PlanetBodyId, d.PlanetTeeth, rp));
        foreach (var p in d.Ports)
        {
            var parent = p.ShaftId == d.SunShaft.Id ? "sun-shaft" : p.ShaftId == d.PlanetShaft.Id ? "planet-shaft" : source is null ? "carrier-shaft" : "source-shaft/" + p.ShaftId;
            poses.Add(new("port/" + p.Id, parent, p.FrameInShaft, Constant(0), p.ShaftId));
        }
        return new(request, numeric, conditions, checks, diagnostics, status, ids, rows, reduced, coordinates, poses, commonC, commonP, relative);
    }
}

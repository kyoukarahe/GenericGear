using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class CarrierAnalyzer
{
    public static CarrierAnalysis Analyze(CarrierDefinition d)
    {
        if (d is null) throw new ArgumentNullException(nameof(d));
        var checks = new List<OrientedDomainCheck>(); var issues = new List<MechanicalDiagnostic>();
        bool Check(string domain, bool ok, string detail)
        {
            checks.Add(new(domain, d.DefinitionId, ok ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (!ok) issues.Add(new("Carrier" + domain, "CarrierAdmission", scope: CarrierProfile.Id, detail: detail));
            return ok;
        }
        CarrierAnalysis Refuse()
        {
            foreach (var domain in new[] { "SourceMechanics", "Ownership", "UnitsAndFrames", "ContactAndReference", "Determinacy", "PitchPlanes" })
                if (!checks.Any(c => c.Domain == domain)) checks.Add(new(domain, d.DefinitionId, OrientedCheckVerdict.NotPerformed, true, "Blocked by failed prerequisite."));
            return new(d, checks, issues);
        }
        foreach (var domain in new[] { "ToothSolids", "SweptSolids", "Dynamics" }.Concat(d.RequiredValidationDomains)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            if (!new[] { "SourceMechanics", "Ownership", "UnitsAndFrames", "ContactAndReference", "Determinacy", "PitchPlanes" }.Contains(domain))
                checks.Add(new(domain, d.DefinitionId, OrientedCheckVerdict.NotPerformed, d.RequiredValidationDomains.Contains(domain), "Outside the bounded ideal pitch-plane profile."));
        var source = d.Source.Definition; var c = source.Shafts.SingleOrDefault(s => s.Id == d.CarrierShaftId);
        bool shape = source.CoaxialLayout is null && source.Connections.Count == 0 && source.KeepOuts.Count == 0 && source.Ports.Count <= 4 && source.Outputs.Count <= 4 &&
            (source.Shafts.Count == 1 && source.Bodies.Count == 0 && source.Contacts.Count == 0 ||
             source.Shafts.Count == 2 && source.Bodies.Count == 2 && source.Contacts.Count == 1 && source.Contacts[0].Kind == OrientedContactKind.ExternalSpur &&
             source.Bodies.Select(b => b.ShaftId).Distinct(StringComparer.Ordinal).Count() == 2);
        var ids = source.Shafts.Select(s => s.Id).Concat(new[] { d.SunShaft.Id, d.PlanetShaft.Id }).ToArray();
        var bodies = source.Bodies.Select(b => b.Id).Concat(new[] { d.CarrierBodyId, d.SunBodyId, d.PlanetBodyId }).ToArray();
        if (!Check("Ownership", shape && c is not null && source.Shafts.Count(s => s.IsPrescribed) == 1 &&
                source.Shafts.Any(s => s.Id == source.RootShaftId && s.IsPrescribed) && !d.SunShaft.IsPrescribed && !d.PlanetShaft.IsPrescribed &&
                ids.Distinct(StringComparer.Ordinal).Count() == ids.Length && bodies.Distinct(StringComparer.Ordinal).Count() == bodies.Length &&
                d.PlanetShaft.CarrierShaftId == d.CarrierShaftId && d.OutputPort.ShaftId == d.PlanetShaft.Id && !source.Ports.Any(p => p.Id == d.OutputPort.Id),
                "One actual borrowed carrier, one ground-held sun, one orbiting planet, bounded standalone/one-stage source and unique ownership required.")) return Refuse();
        var sourceAnalysis = MechanicalAnalyzer.Analyze(d.Source);
        if (!Check("SourceMechanics", sourceAnalysis.IsMechanicallyValid, "Complete current source analysis; source export admission is additionally required at finalization."))
        { issues.AddRange(sourceAnalysis.Diagnostics); return Refuse(); }
        var plane = d.PlaneMm; var local = d.PlanetShaft.FrameInCarrier; var sun = d.SunShaft.Frame;
        var port = d.OutputPort.FrameInShaft; var mapping = d.SourceMapping; var carrier = mapping.FrameMm(c!.Frame);
        bool frames = plane.IsProperCardinal && sun.IsProperCardinal && local.IsProperCardinal && port.IsProperCardinal &&
            mapping.PoseMm.IsProperCardinal && mapping.MillimetersPerSourceUnit > 0 && carrier.IsProperCardinal &&
            carrier.Z.Cross(plane.Z) == default && (plane.Origin - carrier.Origin).Cross(carrier.Z) == default &&
            sun.Z.Cross(plane.Z) == default && sun.Origin == plane.Origin && local.Z.Cross(ExactVector3.UnitZ) == default &&
            port.Z.Cross(ExactVector3.UnitZ) == default && port.Origin.X == 0 && port.Origin.Y == 0 &&
            source.Shafts.All(s => mapping.Direction(s.Frame.Z).Cross(plane.Z) == default);
        bool units = d.Module.Kind == QuantityKind.LinearPosition && d.Module.Value > 0 &&
            new[] { d.CarrierReference, d.SunReference, d.PlanetReference, d.SunMount, d.PlanetMount, d.OutputPort.ReadoutOffset }.All(q => q.Kind == QuantityKind.AngularPosition);
        if (!Check("UnitsAndFrames", frames && units, "Positive mm mapping/module and signed proper parallel frames; port is shaft-local and carrier plane lies on the real input shaft.")) return Refuse();
        var epsC = plane.Z.Dot(carrier.Z); var gammaC = Quarter(plane.X.Dot(carrier.X), plane.Y.Dot(carrier.X));
        var epsS = plane.Z.Dot(sun.Z); var gammaS = Quarter(plane.X.Dot(sun.X), plane.Y.Dot(sun.X));
        var epsP = local.Z.Z; var gammaP = Quarter(local.X.X, local.X.Y);
        var c0 = gammaC + epsC * d.CarrierReference.Value;
        var s0 = gammaS + epsS * (d.SunReference.Value + d.SunMount.Value);
        var p0 = gammaP + epsP * (d.PlanetReference.Value + d.PlanetMount.Value);
        var radiusSun = d.Module.Value * d.SunTeeth / 2; var radiusPlanet = d.Module.Value * d.PlanetTeeth / 2;
        bool contact = d.SunHeld && d.ContactPresent && d.SunTeeth >= 1 && d.SunTeeth <= 4096 && d.PlanetTeeth >= 1 && d.PlanetTeeth <= 4096 &&
            d.ToothRegistration.Denominator == 1 && local.Origin == new ExactVector3(radiusSun + radiusPlanet, 0, 0) &&
            new CarrierMeshConstraint(d.SunTeeth, d.PlanetTeeth, d.ToothRegistration).Residual(s0, c0, p0) == 0;
        if (!Check("ContactAndReference", contact, "External pitch tangency, world hold and integer tooth registration must agree at the explicit reference.")) return Refuse();
        var sourceLaws = sourceAnalysis.AdmittedComponents.SelectMany(x => x.Affine!.Relations).ToDictionary(r => r.DofId, r => r.Relation, StringComparer.Ordinal);
        if (!Check("Determinacy", sourceLaws.Count == source.Shafts.Count && sourceLaws.ContainsKey(c.Id) && sourceLaws[c.Id].Coefficient != 0,
                "All source shafts are driven by the single selected input; hold/contact uniquely determine the added coordinates.")) return Refuse();
        // No sampled collision shortcut: entire moving pair remains in this plane; all prefix pitch planes are distinct.
        if (!Check("PitchPlanes", source.Bodies.All(b => (mapping.PointMm(b.MountingFrame.Origin) - plane.Origin).Dot(plane.Z) != 0),
                "Intended sun/planet tangency is exact for all input. Prefix pitch planes are separated. Arm/shafts/teeth/solids are not certified.")) return Refuse();
        var commonC = sourceLaws[c.Id].Then(epsC, gammaC);
        var commonP = new CarrierMeshConstraint(d.SunTeeth, d.PlanetTeeth, d.ToothRegistration).ResolvePlanet(commonC, new(0, s0));
        var worldP = commonP.Then(epsP, -epsP * gammaP - d.PlanetMount.Value);
        var relativeP = new ExactAffineRelation(worldP.Coefficient - epsP * commonC.Coefficient, worldP.Phase - epsP * commonC.Phase);
        var portLaw = worldP.Then(port.Z.Z, d.OutputPort.ReadoutOffset.Value);
        var poses = new List<CarrierPoseNode>(); var shafts = new List<CarrierShaftLaw>();
        foreach (var shaft in source.Shafts)
        {
            string key = "source-shaft/" + shaft.Id; poses.Add(new(key, null, mapping.FrameMm(shaft.Frame), sourceLaws[shaft.Id], shaft.Id));
            shafts.Add(new(shaft.Id, "FixedAxis", sourceLaws[shaft.Id], null, key));
        }
        foreach (var body in source.Bodies)
        {
            var sf = mapping.FrameMm(source.Shafts.Single(s => s.Id == body.ShaftId).Frame);
            poses.Add(new("source-body/" + body.Id, "source-shaft/" + body.ShaftId, InFrame(sf, mapping.FrameMm(body.MountingFrame)), new(0, 0), body.ShaftId,
                body.Id, body.Teeth, mapping.MillimetersPerSourceUnit * body.OuterPitchRadius));
        }
        poses.Add(new("carrier-frame", null, plane, commonC, c.Id));
        poses.Add(new("sun-shaft", null, sun, new(0, d.SunReference.Value), d.SunShaft.Id));
        poses.Add(new("planet-shaft", "carrier-frame", local, relativeP, d.PlanetShaft.Id));
        shafts.Add(new(d.SunShaft.Id, "GroundHeld", new(0, d.SunReference.Value), null, "sun-shaft"));
        shafts.Add(new(d.PlanetShaft.Id, "Orbiting", worldP, relativeP, "planet-shaft"));
        poses.Add(new("carrier-body", "carrier-frame", OrientedFrame.Identity, new(0, 0), c.Id, d.CarrierBodyId));
        poses.Add(new("sun-body", "sun-shaft", OrientedFrame.Identity, new(0, d.SunMount.Value), d.SunShaft.Id, d.SunBodyId, d.SunTeeth, radiusSun));
        poses.Add(new("planet-body", "planet-shaft", OrientedFrame.Identity, new(0, d.PlanetMount.Value), d.PlanetShaft.Id, d.PlanetBodyId, d.PlanetTeeth, radiusPlanet));
        poses.Add(new("output-port", "planet-shaft", port, new(0, 0), d.PlanetShaft.Id));
        foreach (var law in shafts.Select(s => s.World).Concat(poses.Select(p => p.Rotation)).Append(commonP).Append(portLaw))
        { CarrierProfile.Derived(law.Coefficient); CarrierProfile.Derived(law.Phase); }
        return new(d, checks, issues, shafts, poses, commonC, commonP, portLaw);
    }

    internal static Rational Quarter(Rational x, Rational y) => x == 1 ? Rational.Zero : y == 1 ? new(1, 4) : x == -1 ? new(1, 2) : new(3, 4);
    internal static OrientedFrame InFrame(OrientedFrame parent, OrientedFrame child)
    {
        ExactVector3 V(ExactVector3 v) => new(v.Dot(parent.X), v.Dot(parent.Y), v.Dot(parent.Z));
        return new(V(child.Origin - parent.Origin), V(child.X), V(child.Y), V(child.Z));
    }
}

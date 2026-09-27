using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class MechanicalConnectionQuery
{
    public static ConnectionCompatibilityResult QueryContact(MechanicalDefinition definition, MechanicalContact contact)
    {
        if (definition is null || contact is null) throw new ArgumentNullException();
        var refs = new List<MechanicalReference> { new("Contact", contact.Id), new("Body", contact.BodyAId), new("Body", contact.BodyBId) };
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var diagnostics = new List<MechanicalDiagnostic>();
        var kind = contact.Kind == OrientedContactKind.ExternalSpur ? "ExternalSpurMesh" : "RightAngleBevelMesh";
        ConnectionCompatibilityResult Result(ConnectionCompatibilityVerdict v, Rational? q = null) => new(kind, contact.Id, v, refs, checks, facts, q, diagnostics);
        void Problem(string code, string stage) => diagnostics.Add(new MechanicalDiagnostic(code, stage, related: refs, facts: facts, scope: "LocalConnectionOnly"));
        var a = definition.Bodies.FirstOrDefault(b => b.Id == contact.BodyAId); var b = definition.Bodies.FirstOrDefault(x => x.Id == contact.BodyBId);
        if (a is null || b is null) { Problem("MissingEndpoint", "References"); return Result(ConnectionCompatibilityVerdict.InvalidInput); }
        refs.Add(new MechanicalReference("Shaft", a.ShaftId)); refs.Add(new MechanicalReference("Shaft", b.ShaftId));
        var sa = definition.Shafts.FirstOrDefault(s => s.Id == a.ShaftId); var sb = definition.Shafts.FirstOrDefault(s => s.Id == b.ShaftId);
        if (sa is null || sb is null) { Problem("MissingEndpoint", "References"); return Result(ConnectionCompatibilityVerdict.InvalidInput); }
        void Check(string domain, string subject, bool pass) => checks.Add(new OrientedDomainCheck(domain, subject, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Shared bounded exact local prerequisite."));
        Check("frame", a.Id, a.MountingFrame.IsProperCardinal); Check("frame", b.Id, b.MountingFrame.IsProperCardinal);
        Check("frame", sa.Id, sa.Frame.IsProperCardinal); Check("frame", sb.Id, sb.Frame.IsProperCardinal);
        if (checks.Any(c => c.Verdict == OrientedCheckVerdict.Fail)) { Problem("UnsupportedFrameDomain", "LocalGeometry"); return Result(ConnectionCompatibilityVerdict.Unsupported); }
        Check("mounting", a.Id, MechanicalConnectionPredicates.BodyMounting(a, sa, OrientedTransmissionProfile.MaxTeeth));
        Check("mounting", b.Id, MechanicalConnectionPredicates.BodyMounting(b, sb, OrientedTransmissionProfile.MaxTeeth));
        facts.Add(new MechanicalExactFact("bodyATeeth", null, a.Teeth)); facts.Add(new MechanicalExactFact("bodyBTeeth", null, b.Teeth));
        if (checks.Any(c => c.Verdict == OrientedCheckVerdict.Fail)) { Problem("InvalidBodyMounting", "LocalGeometry"); return Result(ConnectionCompatibilityVerdict.Incompatible); }
        var delta = b.MountingFrame.Origin - a.MountingFrame.Origin;
        facts.Add(new MechanicalExactFact("pitchRadiusPerTooth", a.OuterPitchRadius / a.Teeth, b.OuterPitchRadius / b.Teeth));
        facts.Add(new MechanicalExactFact("axesDot", contact.Kind == OrientedContactKind.RightAngleBevel ? Rational.Zero : (Rational?)null, sa.Frame.Z.Dot(sb.Frame.Z)));
        if (contact.Kind == OrientedContactKind.ExternalSpur)
        {
            facts.Add(new MechanicalExactFact("centerDistanceSquared", (a.OuterPitchRadius + b.OuterPitchRadius) * (a.OuterPitchRadius + b.OuterPitchRadius), delta.LengthSquared));
            facts.Add(new MechanicalExactFact("planeOffset", 0, delta.Dot(sa.Frame.Z)));
            facts.Add(new MechanicalExactFact("axesCrossSquared", 0, sa.Frame.Z.Cross(sb.Frame.Z).LengthSquared));
        }
        else if (contact.Cone is not null)
        {
            var cone = contact.Cone;
            facts.Add(new MechanicalExactFact("axisApexResidualA", 0, (cone.Apex - sa.Frame.Origin).Cross(sa.Frame.Z).LengthSquared));
            facts.Add(new MechanicalExactFact("axisApexResidualB", 0, (cone.Apex - sb.Frame.Origin).Cross(sb.Frame.Z).LengthSquared));
            facts.Add(new MechanicalExactFact("innerParameter", null, cone.InnerParameter));
            facts.Add(new MechanicalExactFact("outerScale", cone.OuterScaleA, cone.OuterScaleB));
        }
        var local = MechanicalConnectionPredicates.Contact(contact.Id, contact.Kind, a, b, sa, sb, contact.Cone); checks.AddRange(local.Checks);
        if (!local.IsCompatible)
        {
            Problem(a.OuterPitchRadius / a.Teeth != b.OuterPitchRadius / b.Teeth ? "PitchScaleMismatch" : "InvalidMeshGeometry", "LocalGeometry");
            return Result(ConnectionCompatibilityVerdict.Incompatible);
        }
        return Result(ConnectionCompatibilityVerdict.CompatibleWithinProfile, local.Transfer);
    }

    public static ConnectionCompatibilityResult QueryPortConnection(MechanicalDefinition definition, ShaftPortConnection connection)
    {
        if (definition is null || connection is null) throw new ArgumentNullException();
        var refs = new List<MechanicalReference> { new("Connection", connection.Id), new("Port", connection.PortAId), new("Port", connection.PortBId) };
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var diagnostics = new List<MechanicalDiagnostic>();
        ConnectionCompatibilityResult Result(ConnectionCompatibilityVerdict v, string? code = null, Rational? transfer = null)
        {
            if (code is not null) diagnostics.Add(new MechanicalDiagnostic(code, "LocalPortConnection", related: refs, facts: facts, scope: "LocalConnectionOnly"));
            return new ConnectionCompatibilityResult("RigidZeroPhasePortConnection", connection.Id, v, refs, checks, facts, transfer, diagnostics);
        }
        var a = definition.Ports.FirstOrDefault(p => p.Id == connection.PortAId); var b = definition.Ports.FirstOrDefault(p => p.Id == connection.PortBId);
        if (a is null || b is null) return Result(ConnectionCompatibilityVerdict.InvalidInput, "MissingEndpoint");
        refs.Add(new MechanicalReference("Shaft", a.ShaftId)); refs.Add(new MechanicalReference("Shaft", b.ShaftId));
        var sa = definition.Shafts.FirstOrDefault(s => s.Id == a.ShaftId); var sb = definition.Shafts.FirstOrDefault(s => s.Id == b.ShaftId);
        if (sa is null || sb is null) return Result(ConnectionCompatibilityVerdict.InvalidInput, "MissingEndpoint");
        void Check(string domain, string subject, bool pass) => checks.Add(new OrientedDomainCheck(domain, subject, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Shared exact zero-phase port prerequisite."));
        Check("frame", a.Id, a.Frame.IsProperCardinal && sa.Frame.IsProperCardinal); Check("frame", b.Id, b.Frame.IsProperCardinal && sb.Frame.IsProperCardinal);
        facts.Add(new MechanicalExactFact("phaseA", 0, a.PhaseOffset)); facts.Add(new MechanicalExactFact("phaseB", 0, b.PhaseOffset));
        if (a.Kind != ShaftConnectionKind.RigidZeroPhase || b.Kind != ShaftConnectionKind.RigidZeroPhase || a.PhaseOffset != 0 || b.PhaseOffset != 0)
            return Result(ConnectionCompatibilityVerdict.Unsupported, "UnsupportedPhaseDomain");
        if (checks.Any(c => c.Verdict == OrientedCheckVerdict.Fail)) return Result(ConnectionCompatibilityVerdict.Unsupported, "UnsupportedFrameDomain");
        Check("port", a.Id, MechanicalConnectionPredicates.PortMounting(a, sa)); Check("port", b.Id, MechanicalConnectionPredicates.PortMounting(b, sb));
        facts.Add(new MechanicalExactFact("stationDistanceSquared", 0, (a.Frame.Origin - b.Frame.Origin).LengthSquared));
        facts.Add(new MechanicalExactFact("coordinateTransfer", a.Frame.Z.Dot(b.Frame.Z), connection.CoordinateTransfer));
        Check("rigid-port-geometry", connection.Id, MechanicalConnectionPredicates.RigidPortGeometry(a, b, connection.CoordinateTransfer));
        if (checks.All(c => c.Verdict == OrientedCheckVerdict.Pass)) facts.Add(new MechanicalExactFact("shaftTransfer", null,
            MechanicalConnectionPredicates.RigidShaftTransfer(sa, a, sb, b, connection.CoordinateTransfer)));
        return checks.All(c => c.Verdict == OrientedCheckVerdict.Pass)
            ? Result(ConnectionCompatibilityVerdict.CompatibleWithinProfile, transfer: a.Frame.Z.Dot(b.Frame.Z))
            : Result(ConnectionCompatibilityVerdict.Incompatible, "InvalidPortConnection");
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Layout;

public sealed class LocalContactGeometry
{
    internal LocalContactGeometry(Rational transfer, IEnumerable<OrientedDomainCheck> checks)
    { Transfer = transfer; Checks = checks.ToList().AsReadOnly(); }
    public Rational Transfer { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public bool IsCompatible => !Transfer.IsZero && Checks.All(c => c.Verdict == OrientedCheckVerdict.Pass);
}

/// <summary>Shared exact local predicates used by existing artifact validation and editable-definition queries.</summary>
public static class MechanicalConnectionPredicates
{
    public static bool BodyMounting(OrientedGearBody body, OrientedShaft shaft, int maxTeeth) =>
        body.Teeth > 0 && body.Teeth <= maxTeeth && body.OuterPitchRadius > 0 &&
        shaft.Contains(body.MountingFrame.Origin) && shaft.Frame.Z.Cross(body.MountingFrame.Z) == default && shaft.Frame.X == body.MountingFrame.X;
    public static bool PortMounting(ShaftPort port, OrientedShaft shaft) =>
        port.Kind == ShaftConnectionKind.RigidZeroPhase && port.PhaseOffset == 0 && shaft.Contains(port.Frame.Origin) &&
        shaft.Frame.Z.Cross(port.Frame.Z) == default && shaft.Frame.X == port.Frame.X;
    public static bool RigidPortConnection(ShaftPort a, ShaftPort b, Rational coordinateTransfer) =>
        a.ShaftId == b.ShaftId && RigidPortGeometry(a, b, coordinateTransfer);
    /// <summary>Local geometric compatibility is broader than the legacy export's already-unified shaft representation.</summary>
    public static bool RigidPortGeometry(ShaftPort a, ShaftPort b, Rational coordinateTransfer) =>
        a.Id != b.Id && a.Frame.Origin == b.Frame.Origin &&
        a.Frame.Z.Cross(b.Frame.Z) == default && a.Frame.X == b.Frame.X && coordinateTransfer == a.Frame.Z.Dot(b.Frame.Z);
    /// <summary>Converts an admitted port-coordinate relation back to its two shaft coordinates; no shaft objects are merged.</summary>
    public static Rational RigidShaftTransfer(OrientedShaft shaftA, ShaftPort portA, OrientedShaft shaftB, ShaftPort portB, Rational portTransfer) =>
        shaftA.Frame.Z.Dot(portA.Frame.Z) * portTransfer / shaftB.Frame.Z.Dot(portB.Frame.Z);

    /// <summary>Call only after endpoint, proper-frame, positive pitch and body-mounting admission.</summary>
    public static LocalContactGeometry Contact(string id, OrientedContactKind kind, OrientedGearBody a, OrientedGearBody b,
        OrientedShaft sa, OrientedShaft sb, RightAnglePitchCone? cone)
    {
        if (a.Teeth <= 0 || b.Teeth <= 0 || a.OuterPitchRadius <= 0 || b.OuterPitchRadius <= 0)
            throw new ArgumentException("Positive pitch dimensions are prerequisites.");
        var checks = new List<OrientedDomainCheck>(); Rational transfer = 0;
        void Check(string domain, string subject, bool valid, string detail) => checks.Add(new OrientedDomainCheck(domain, subject,
            valid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
        if (kind == OrientedContactKind.ExternalSpur)
        {
            var delta = b.MountingFrame.Origin - a.MountingFrame.Origin; var sum = a.OuterPitchRadius + b.OuterPitchRadius;
            var valid = cone is null && a.Kind == OrientedGearKind.PlanarSpur && b.Kind == OrientedGearKind.PlanarSpur &&
                sa.Frame.Z.Cross(sb.Frame.Z) == default && delta.Dot(sa.Frame.Z) == 0 && delta.LengthSquared == sum * sum &&
                a.OuterPitchRadius / a.Teeth == b.OuterPitchRadius / b.Teeth && a.ShaftId != b.ShaftId;
            Check("spur-geometry", id, valid, "Coplanar external pitch circles, exact radii/tooth scale, tangent without interior overlap.");
            transfer = -sa.Frame.Z.Dot(sb.Frame.Z) * a.OuterPitchRadius / b.OuterPitchRadius;
            return new LocalContactGeometry(transfer, checks);
        }
        if (kind != OrientedContactKind.RightAngleBevel) throw new NotSupportedException("Unsupported contact geometry.");
        Check("cone-geometry", id, cone is not null && a.Kind == OrientedGearKind.RightAngleBevel && b.Kind == OrientedGearKind.RightAngleBevel,
            "Typed bevel bodies and finite pitch-cone data are required.");
        if (cone is null) return new LocalContactGeometry(0, checks);
        var r1 = a.OuterPitchRadius; var r2 = b.OuterPitchRadius;
        var centers = a.MountingFrame.Origin == cone.Apex + cone.OutwardA * r2 && b.MountingFrame.Origin == cone.Apex + cone.OutwardB * r1;
        var directions = cone.OutwardA.IsCardinal && cone.OutwardB.IsCardinal && cone.OutwardA.Dot(cone.OutwardB) == 0 &&
            sa.Frame.Z.Cross(cone.OutwardA) == default && sb.Frame.Z.Cross(cone.OutwardB) == default;
        Check("axis-intersection", id, sa.Contains(cone.Apex) && sb.Contains(cone.Apex) && sa.Frame.Z.Dot(sb.Frame.Z) == 0,
            "Common apex lies on both actual lines; perpendicular but skew lines are rejected.");
        var geometry = directions && centers && cone.InnerParameter > 0 && cone.InnerParameter < 1 &&
            cone.OuterScaleA > 0 && cone.OuterScaleA == cone.OuterScaleB && r1 == cone.OuterScaleA * a.Teeth && r2 == cone.OuterScaleB * b.Teeth &&
            cone.OuterContact == cone.Apex + cone.OutwardA * r2 + cone.OutwardB * r1 &&
            (cone.OuterContact - a.MountingFrame.Origin).LengthSquared == r1 * r1 && (cone.OuterContact - b.MountingFrame.Origin).LengthSquared == r2 * r2;
        Check("cone-geometry", id + "/pitch", geometry, "Exact centers, common outer radius-per-tooth scale, contact Q and 0<lambda<1; R^2=" + cone.ConeDistanceSquared);
        if (!geometry) return new LocalContactGeometry(0, checks);
        var generator = cone.OuterContact - cone.Apex; transfer = VelocityTransfer(sa.Frame.Z, sb.Frame.Z, generator);
        var e1 = sa.Frame.Z.Dot(cone.OutwardA); var e2 = sb.Frame.Z.Dot(cone.OutwardB);
        Check("rolling-relation", id, transfer == -e1 * e2 * new Rational(a.Teeth, b.Teeth),
            "Transfer derived from cross-product velocities, independently agrees with signed tooth equation.");
        var n1 = cone.OutwardB * r1 - cone.OutwardA * (r1 * r1 / r2); var n2 = cone.OutwardA * r2 - cone.OutwardB * (r2 * r2 / r1);
        Check("intended-surface-contact", id, n1.Cross(n2) == default && n1.Dot(n2) < 0,
            "Complementary right-angle cones have opposite tangent normals on the common generator; finite intervals share that contact only, not tooth-solid proof.");
        foreach (var t in new[] { cone.InnerParameter, (cone.InnerParameter + 1) / 2, Rational.One })
        {
            var v1 = sa.Frame.Z.Cross(generator * t); var v2 = sb.Frame.Z.Cross(generator * t) * transfer;
            Check("contact-velocity", id + "/" + t, v1 == v2, "vA=" + v1 + ";vB=" + v2 + ";residual=" + (v1 - v2));
        }
        Check("contact-velocity", id + "/whole-interval", sa.Frame.Z.Cross(generator) == sb.Frame.Z.Cross(generator) * transfer,
            "vA(t)-vB(t)=t*(aA cross g - q*aB cross g)=0 for every t in [lambda,1]; samples are supplemental, not the proof.");
        return new LocalContactGeometry(transfer, checks);
    }
    public static Rational VelocityTransfer(ExactVector3 positiveA, ExactVector3 positiveB, ExactVector3 generator)
    {
        var a = positiveA.Cross(generator); var b = positiveB.Cross(generator);
        if (b.LengthSquared.IsZero) throw new ArgumentException("Degenerate contact velocity.");
        var q = a.Dot(b) / b.LengthSquared;
        if (a != b * q || q.IsZero) throw new ArgumentException("Contact velocities are not a nonzero scalar multiple.");
        return q;
    }
}

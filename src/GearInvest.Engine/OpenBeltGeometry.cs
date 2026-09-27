using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Device-local construction a+b*sqrt(w), with the positive branch. Not a general algebraic-number API.</summary>
public sealed class OpenBeltRadicalVector
{
    internal OpenBeltRadicalVector(ExactVector3 rationalPart, ExactVector3 radicalCoefficient, Rational radicand)
    {
        OpenBeltGeometry.Bound(rationalPart); OpenBeltGeometry.Bound(radicalCoefficient); MechanicalDerivedNumbers.Check(radicand);
        RationalPart = rationalPart; RadicalCoefficient = radicalCoefficient; Radicand = radicand;
    }
    public ExactVector3 RationalPart { get; }
    public ExactVector3 RadicalCoefficient { get; }
    public Rational Radicand { get; }
    public string SquareRootBranch => "positive";
    public string CanonicalRepresentation => Pack(V(RationalPart), V(RadicalCoefficient), F(Radicand), SquareRootBranch);
    internal OpenBeltRadicalVector Offset(ExactVector3 v) => new(RationalPart + v, RadicalCoefficient, Radicand);
    internal OpenBeltRadicalVector Scale(Rational q) => new(RationalPart * q, RadicalCoefficient * q, Radicand);
    internal OpenBeltRadicalVector CrossLeft(ExactVector3 v) => new(v.Cross(RationalPart), v.Cross(RadicalCoefficient), Radicand);
    internal OpenBeltRadicalVector Minus(OpenBeltRadicalVector other)
    {
        if (Radicand != other.Radicand) throw new ArgumentException("Only one current route radical is supported.");
        return new(RationalPart - other.RationalPart, RadicalCoefficient - other.RadicalCoefficient, Radicand);
    }
    internal (Rational Rational, Rational Radical) Dot(OpenBeltRadicalVector other)
    {
        if (Radicand != other.Radicand) throw new ArgumentException("Only one current route radical is supported.");
        return (RationalPart.Dot(other.RationalPart) + RadicalCoefficient.Dot(other.RadicalCoefficient) * Radicand,
            RationalPart.Dot(other.RadicalCoefficient) + RadicalCoefficient.Dot(other.RationalPart));
    }
}

public sealed class OpenBeltSpanDescriptor
{
    internal OpenBeltSpanDescriptor(string id, OpenBeltRadicalVector start, OpenBeltRadicalVector end, Rational lengthSquared)
    { Id = id; Start = start; End = end; LengthSquared = lengthSquared; }
    public string Id { get; }
    public OpenBeltRadicalVector Start { get; }
    public OpenBeltRadicalVector End { get; }
    public Rational LengthSquared { get; }
    public string LengthSquareRootBranch => "positive";
    public string CanonicalRepresentation => Pack(Id, Start.CanonicalRepresentation, End.CanonicalRepresentation, F(LengthSquared), LengthSquareRootBranch);
}

/// <summary>Clockwise circular sweep pi + AsinCoefficient*asin(h), through the specified outer cardinal ray.</summary>
public sealed class OpenBeltArcDescriptor
{
    internal OpenBeltArcDescriptor(string id, ExactVector3 center, Rational radius, OpenBeltRadicalVector start,
        OpenBeltRadicalVector end, ExactVector3 throughDirection, int asinCoefficient)
    { Id = id; CenterMm = center; RadiusMm = radius; Start = start; End = end; ThroughDirection = throughDirection; AsinCoefficient = asinCoefficient; }
    public string Id { get; }
    public ExactVector3 CenterMm { get; }
    public Rational RadiusMm { get; }
    public OpenBeltRadicalVector Start { get; }
    public OpenBeltRadicalVector End { get; }
    public ExactVector3 ThroughDirection { get; }
    public bool Clockwise => true;
    public int PiCoefficient => 1;
    public int AsinCoefficient { get; }
    public string SweepBranch => "outer-clockwise";
    public string CanonicalRepresentation => Pack(Id, V(CenterMm), F(RadiusMm), Start.CanonicalRepresentation, End.CanonicalRepresentation,
        V(ThroughDirection), N(AsinCoefficient), SweepBranch);
}

/// <summary>Exact bounded recipe 2*sqrt(spanSquared) + pi*piCoefficient + asinCoefficient*asin(asinArgument).</summary>
public sealed class OpenBeltLengthDescriptor
{
    internal OpenBeltLengthDescriptor(Rational distance, Rational inputRadius, Rational outputRadius)
    {
        if (!OpenBeltGeometry.InDomain(distance, inputRadius, outputRadius)) throw new ArgumentException("Strict separated positive pulley geometry is required.");
        CenterDistance = distance; InputRadius = inputRadius; OutputRadius = outputRadius;
        RadiusDifference = inputRadius - outputRadius; SpanLengthSquared = distance * distance - RadiusDifference * RadiusDifference;
        PiCoefficient = inputRadius + outputRadius; AsinArgument = RadiusDifference / distance; AsinCoefficient = 2 * RadiusDifference;
        foreach (var value in new[] { distance, inputRadius, outputRadius, RadiusDifference, SpanLengthSquared, PiCoefficient, AsinArgument, AsinCoefficient }) MechanicalDerivedNumbers.Check(value);
        DescriptorId = HashText(CanonicalRepresentation);
    }
    public string Semantics => OpenBeltProfile.LengthSemantics;
    public string DescriptorId { get; }
    public Rational CenterDistance { get; }
    public Rational InputRadius { get; }
    public Rational OutputRadius { get; }
    public Rational RadiusDifference { get; }
    public Rational SpanLengthSquared { get; }
    public int SpanSquareRootCoefficient => 2;
    public string SquareRootBranch => "positive";
    public Rational PiCoefficient { get; }
    public Rational AsinArgument { get; }
    public Rational AsinCoefficient { get; }
    public string AsinBranch => "principal-minus-half-pi-to-half-pi";
    public string CanonicalRepresentation => Pack(Semantics, F(CenterDistance), F(InputRadius), F(OutputRadius), F(RadiusDifference),
        F(SpanLengthSquared), F(PiCoefficient), F(AsinArgument), F(AsinCoefficient), SquareRootBranch, AsinBranch);
}

/// <summary>Coefficient-wise exact witness in the current construction radical, or an exact branch/domain predicate.</summary>
public sealed class OpenBeltExactProof
{
    internal OpenBeltExactProof(string id, string scope, Rational rationalResidual, Rational radicalResidual,
        Rational radicand, bool predicate, string detail)
    {
        foreach (var r in new[] { rationalResidual, radicalResidual, radicand }) MechanicalDerivedNumbers.Check(r);
        Id = id; Scope = scope; RationalResidual = rationalResidual; RadicalResidual = radicalResidual; Radicand = radicand;
        IsVerified = predicate && rationalResidual == 0 && radicalResidual == 0; Detail = detail;
    }
    public string Id { get; }
    public string Scope { get; }
    public Rational RationalResidual { get; }
    public Rational RadicalResidual { get; }
    public Rational Radicand { get; }
    public bool IsVerified { get; }
    public string Detail { get; }
    public string CanonicalRepresentation => Pack(Id, Scope, F(RationalResidual), F(RadicalResidual), F(Radicand), OpenBeltProfile.Flag(IsVerified), Detail);
}

public sealed class OpenBeltRouteDescriptor
{
    internal OpenBeltRouteDescriptor(ExactVector3 c1, ExactVector3 c2, ExactQuantity r1, ExactQuantity r2, ExactVector3 normal,
        ExactVector3 e, ExactVector3 f, Rational distance, Rational h, Rational w,
        OpenBeltRadicalVector plus, OpenBeltRadicalVector minus, IEnumerable<OpenBeltExactProof> proofs)
    {
        InputCenterMm = c1; OutputCenterMm = c2; InputPitchRadius = r1; OutputPitchRadius = r2; Normal = normal;
        CenterDirection = e; SideDirection = f; CenterDistance = distance; H = h; Radicand = w; Nplus = plus; Nminus = minus;
        P1plus = plus.Scale(r1.Value).Offset(c1); P2plus = plus.Scale(r2.Value).Offset(c2);
        P1minus = minus.Scale(r1.Value).Offset(c1); P2minus = minus.Scale(r2.Value).Offset(c2);
        Length = new OpenBeltLengthDescriptor(distance, r1.Value, r2.Value);
        PlusSpan = new OpenBeltSpanDescriptor("plus", P1plus, P2plus, Length.SpanLengthSquared);
        MinusSpan = new OpenBeltSpanDescriptor("minus", P2minus, P1minus, Length.SpanLengthSquared);
        OutputArc = new OpenBeltArcDescriptor("output", c2, r2.Value, P2plus, P2minus, e, -2);
        InputArc = new OpenBeltArcDescriptor("input", c1, r1.Value, P1minus, P1plus, -e, 2);
        Proofs = proofs.OrderBy(p => p.Id, StringComparer.Ordinal).ToList().AsReadOnly(); RouteId = HashText(CanonicalRepresentation);
    }
    public string NumericSemantics => OpenBeltProfile.NumericSemantics;
    public string RouteId { get; }
    public ExactVector3 InputCenterMm { get; }
    public ExactVector3 OutputCenterMm { get; }
    public ExactQuantity InputPitchRadius { get; }
    public ExactQuantity OutputPitchRadius { get; }
    public ExactVector3 Normal { get; }
    public ExactVector3 CenterDirection { get; }
    public ExactVector3 SideDirection { get; }
    public Rational CenterDistance { get; }
    public Rational H { get; }
    public Rational Radicand { get; }
    public string SquareRootBranch => "positive";
    public string Traversal => "P1plus>P2plus>outer-output-clockwise>P2minus>P1minus>outer-input-clockwise>P1plus";
    public OpenBeltRadicalVector Nplus { get; }
    public OpenBeltRadicalVector Nminus { get; }
    public OpenBeltRadicalVector P1plus { get; }
    public OpenBeltRadicalVector P2plus { get; }
    public OpenBeltRadicalVector P2minus { get; }
    public OpenBeltRadicalVector P1minus { get; }
    public OpenBeltSpanDescriptor PlusSpan { get; }
    public OpenBeltSpanDescriptor MinusSpan { get; }
    public OpenBeltArcDescriptor InputArc { get; }
    public OpenBeltArcDescriptor OutputArc { get; }
    public OpenBeltLengthDescriptor Length { get; }
    public ReadOnlyCollection<OpenBeltExactProof> Proofs { get; }
    public bool HasExactProof => Proofs.Count > 0 && Proofs.All(p => p.IsVerified);
    public string CanonicalRepresentation => Pack(NumericSemantics, V(InputCenterMm), V(OutputCenterMm), OpenBeltProfile.Q(InputPitchRadius),
        OpenBeltProfile.Q(OutputPitchRadius), V(Normal), V(CenterDirection), V(SideDirection), F(CenterDistance), F(H), F(Radicand), SquareRootBranch,
        Nplus.CanonicalRepresentation, Nminus.CanonicalRepresentation, PlusSpan.CanonicalRepresentation, MinusSpan.CanonicalRepresentation,
        InputArc.CanonicalRepresentation, OutputArc.CanonicalRepresentation, Length.CanonicalRepresentation, Traversal,
        Pack(Proofs.Select(p => p.CanonicalRepresentation).ToArray()));
}

/// <summary>Exact cardinal open-route construction and bounded length proofs. Display sqrt/asin is deliberately elsewhere.</summary>
public static class OpenBeltGeometry
{
    public static OpenBeltRouteDescriptor CreateRoute(ExactVector3 inputCenterMm, ExactVector3 outputCenterMm,
        ExactQuantity inputPitchRadius, ExactQuantity outputPitchRadius, ExactVector3 normal)
    {
        Bound(inputCenterMm); Bound(outputCenterMm); Bound(normal);
        if (inputPitchRadius.Kind != QuantityKind.LinearPosition || outputPitchRadius.Kind != QuantityKind.LinearPosition)
            throw new ArgumentException("DimensionMismatch: pulley pitch radii must be lengths.");
        var delta = outputCenterMm - inputCenterMm; var d = Abs(delta.X) + Abs(delta.Y) + Abs(delta.Z);
        if (d == 0) throw new ArgumentException("Coincident pulley centers.");
        var e = delta * (Rational.One / d); var f = normal.Cross(e);
        if (!normal.IsCardinal || !e.IsCardinal || normal.Dot(e) != 0 || !f.IsCardinal || e.Cross(f) != normal)
            throw new ArgumentException("A proper cardinal coplanar route basis is required.");
        var r1 = inputPitchRadius.Value; var r2 = outputPitchRadius.Value;
        if (!InDomain(d, r1, r2)) throw new ArgumentException("Pulley pitch disks must be strictly separated: D > R1 + R2 > 0.");
        var h = (r1 - r2) / d; var w = 1 - h * h; MechanicalDerivedNumbers.Check(h); MechanicalDerivedNumbers.Check(w);
        var plus = new OpenBeltRadicalVector(e * h, f, w); var minus = new OpenBeltRadicalVector(e * h, -f, w);
        var p1p = plus.Scale(r1).Offset(inputCenterMm); var p2p = plus.Scale(r2).Offset(outputCenterMm);
        var p1m = minus.Scale(r1).Offset(inputCenterMm); var p2m = minus.Scale(r2).Offset(outputCenterMm);
        var spanSquared = d * d - (r1 - r2) * (r1 - r2); var proofs = new List<OpenBeltExactProof>();
        void Scalar(string id, string scope, (Rational Rational, Rational Radical) v, Rational expected, string detail) =>
            proofs.Add(new OpenBeltExactProof(id, scope, v.Rational - expected, v.Radical, w, true, detail));
        proofs.Add(new OpenBeltExactProof("positive-radical", "Branch", 0, 0, w, w > 0 && h > -1 && h < 1,
            "D>R1+R2 and both radii positive imply -1<h<1 and sqrt(1-h*h)>0."));
        Scalar("normal-plus", "UnitNormal", plus.Dot(plus), 1, "|h*e+sqrt(w)*f|^2=h^2+w=1.");
        Scalar("normal-minus", "UnitNormal", minus.Dot(minus), 1, "|h*e-sqrt(w)*f|^2=h^2+w=1.");
        foreach (var row in new[] { (Id: "plus", A: p1p, B: p2p), (Id: "minus", A: p1m, B: p2m) })
        {
            var a = row.A.Offset(-inputCenterMm); var b = row.B.Offset(-outputCenterMm); var span = row.B.Minus(row.A);
            Scalar(row.Id + "/input-circle", "PitchCircle", a.Dot(a), r1 * r1, "Exact endpoint radius squared.");
            Scalar(row.Id + "/output-circle", "PitchCircle", b.Dot(b), r2 * r2, "Exact endpoint radius squared.");
            Scalar(row.Id + "/input-tangent", "ExternalTangent", span.Dot(a), 0, "Span is perpendicular to the input radius.");
            Scalar(row.Id + "/output-tangent", "ExternalTangent", span.Dot(b), 0, "Span is perpendicular to the output radius.");
            Scalar(row.Id + "/span-length", "SpanLength", span.Dot(span), spanSquared, "Both external free spans have D^2-(R1-R2)^2 squared length.");
        }
        proofs.Add(new OpenBeltExactProof("outer-arcs", "WrapBranch", 0, 0, w, h > -1 && h < 1,
            "Principal asin(h) lies in (-pi/2,pi/2): input pi+2asin(h) through -e and output pi-2asin(h) through +e are positive clockwise outer sweeps, not always short arcs."));
        // Every arc endpoint is the very same constructed object used by the adjacent span.
        var closure = p2p.Minus(p1p).Minus(p2p.Minus(p2m)).Minus(p2m.Minus(p1m)).Minus(p1m.Minus(p1p));
        AddVectorProof(proofs, "closed-loop", "ClosedRoute", closure, "Ordered span/outer-arc endpoint displacements telescope exactly to zero.");
        var route = new OpenBeltRouteDescriptor(inputCenterMm, outputCenterMm, inputPitchRadius, outputPitchRadius, normal, e, f, d, h, w, plus, minus, proofs);
        if (!route.HasExactProof) throw new InvalidOperationException("Internal open-belt construction proof failed.");
        return route;
    }

    public static bool VerifyRoute(OpenBeltRouteDescriptor route)
    {
        if (route is null) throw new ArgumentNullException(nameof(route));
        var actual = CreateRoute(route.InputCenterMm, route.OutputCenterMm, route.InputPitchRadius, route.OutputPitchRadius, route.Normal);
        return actual.CanonicalRepresentation == route.CanonicalRepresentation && route.HasExactProof;
    }

    public static OpenBeltLengthSpecification CreateBeltForCurrentRoute(OpenBeltRouteDescriptor route)
    {
        if (route is null) throw new ArgumentNullException(nameof(route));
        if (!VerifyRoute(route)) throw new ArgumentException("Only a currently reconstructed exact route can propose a belt length.");
        return new OpenBeltLengthSpecification(ExactQuantity.FromCanonical(QuantityKind.LinearPosition, route.CenterDistance), route.InputPitchRadius, route.OutputPitchRadius);
    }

    public static OpenBeltLengthCompatibility CompareLength(OpenBeltRouteDescriptor? route, OpenBeltLengthSpecification? selected)
    {
        var facts = new List<MechanicalExactFact>(); OpenBeltLengthDescriptor? reference = null;
        OpenBeltLengthCompatibility Result(OpenBeltLengthVerdict verdict, string proof, string detail) =>
            new(verdict, proof, selected, route?.Length, reference, facts, detail);
        if (route is null || !VerifyRoute(route)) return Result(OpenBeltLengthVerdict.InvalidSpecification, "InvalidCurrentRoute", "Current strict route geometry is a prerequisite to any length proof.");
        if (selected is null) return Result(OpenBeltLengthVerdict.InvalidSpecification, "MissingSelectedBelt", "An explicit immutable belt length selection is required.");
        var qs = new[] { selected.CenterDistance, selected.InputPitchRadius, selected.OutputPitchRadius };
        if (selected.Semantics != OpenBeltProfile.LengthSemantics || qs.Any(q => q.Kind != QuantityKind.LinearPosition) ||
            !InDomain(qs[0].Value, qs[1].Value, qs[2].Value))
            return Result(OpenBeltLengthVerdict.InvalidSpecification, "InvalidReferenceGeometry", "Reference semantics, dimensions and D>R1+R2>0 must be valid before monotonicity is used.");
        var d = route.CenterDistance; var r1 = route.InputPitchRadius.Value; var r2 = route.OutputPitchRadius.Value;
        var sd = qs[0].Value; var s1 = qs[1].Value; var s2 = qs[2].Value; reference = new OpenBeltLengthDescriptor(sd, s1, s2);
        facts.Add(new MechanicalExactFact("selectedReferenceDistance.mm", sd, d));
        facts.Add(new MechanicalExactFact("selectedReferenceInputRadius.mm", s1, r1));
        facts.Add(new MechanicalExactFact("selectedReferenceOutputRadius.mm", s2, r2));
        var samePair = r1 == s1 && r2 == s2 || r1 == s2 && r2 == s1;
        if (d == sd && samePair) return Result(OpenBeltLengthVerdict.ExactMatch, "EqualDistanceUnorderedRadii",
            "The length expression is invariant under radius interchange; tuple order is not route or specification identity.");
        OpenBeltLengthCompatibility Monotonic(Rational now, Rational was, string proof, string detail) =>
            Result(now > was ? OpenBeltLengthVerdict.ProvenTooShort : OpenBeltLengthVerdict.ProvenTooLong, proof, detail);
        if (samePair) return Monotonic(d, sd, "StrictCenterDistanceMonotonicity",
            "Both endpoints are in the strict domain; with fixed radii the entire distance interval stays in-domain and dL/dD=2*sqrt(D^2-(R1-R2)^2)/D>0.");
        if (d == sd)
        {
            Rational changed, prior;
            if (r1 == s1) { changed = r2; prior = s2; }
            else if (r1 == s2) { changed = r2; prior = s1; }
            else if (r2 == s1) { changed = r1; prior = s2; }
            else if (r2 == s2) { changed = r1; prior = s1; }
            else return Result(OpenBeltLengthVerdict.Inconclusive, "UnsupportedMultiParameterChange", "No single-radius or fixed-radius distance proof applies; different reference tuples do not prove unequal length.");
            facts.Add(new MechanicalExactFact("singleChangedRadius.mm", prior, changed));
            return Monotonic(changed, prior, "StrictSingleRadiusMonotonicity",
                "Match one common radius including multiplicity and swapped labels. The strict domain holds on the segment; dL/dRi=pi+/-2asin(h)>0 throughout it.");
        }
        return Result(OpenBeltLengthVerdict.Inconclusive, "UnsupportedMultiParameterChange", "Multiple independent changes require a length comparison outside this bounded exact proof domain; no rounded comparison is substituted.");
    }

    internal static ReadOnlyCollection<OpenBeltExactProof> ContactVelocityProofs(OpenBeltRouteDescriptor route, ExactVector3 inputAxis, ExactVector3 outputAxis, Rational transfer)
    {
        var proofs = new List<OpenBeltExactProof>();
        foreach (var row in new[] { (Id: "plus", A: route.P1plus, B: route.P2plus), (Id: "minus", A: route.P1minus, B: route.P2minus) })
        {
            var v1 = row.A.Offset(-route.InputCenterMm).CrossLeft(inputAxis);
            var v2 = row.B.Offset(-route.OutputCenterMm).CrossLeft(outputAxis).Scale(transfer);
            AddVectorProof(proofs, row.Id + "/contact-velocity", "IdealNoSlipRelation", v1.Minus(v2),
                "After removing the common 2*pi factor, a1 cross radius1 = qBelt*(a2 cross radius2) at both endpoints of this free span.");
            var normal = row.Id == "plus" ? route.Nplus : route.Nminus;
            var routeTangent = normal.CrossLeft(route.Normal).Scale(-1);
            var materialRate = routeTangent.Scale(-route.InputPitchRadius.Value * inputAxis.Dot(route.Normal));
            AddVectorProof(proofs, row.Id + "/clockwise-transport", "MaterialTransport", v1.Minus(materialRate),
                "Actual pitch tangent velocity agrees with clockwise transport -2*pi*R1*epsilon1 per retained input turn; route tangent locations are fixed.");
        }
        return proofs.OrderBy(p => p.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    internal static bool InDomain(Rational d, Rational r1, Rational r2) => r1 > 0 && r2 > 0 && d > r1 + r2;
    internal static Rational Abs(Rational v) => v < 0 ? -v : v;
    internal static void Bound(ExactVector3 v) { MechanicalDerivedNumbers.Check(v.X); MechanicalDerivedNumbers.Check(v.Y); MechanicalDerivedNumbers.Check(v.Z); }
    private static void AddVectorProof(ICollection<OpenBeltExactProof> proofs, string id, string scope, OpenBeltRadicalVector residual, string detail)
    {
        proofs.Add(new OpenBeltExactProof(id + "/x", scope, residual.RationalPart.X, residual.RadicalCoefficient.X, residual.Radicand, true, detail));
        proofs.Add(new OpenBeltExactProof(id + "/y", scope, residual.RationalPart.Y, residual.RadicalCoefficient.Y, residual.Radicand, true, detail));
        proofs.Add(new OpenBeltExactProof(id + "/z", scope, residual.RationalPart.Z, residual.RadicalCoefficient.Z, residual.Radicand, true, detail));
    }
}

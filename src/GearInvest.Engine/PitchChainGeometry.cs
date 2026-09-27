using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>The positive radius p/(2 sin(pi/Z)); not a rational radius or circular pitch.</summary>
public sealed class RegularSprocketPitchDescriptor
{
    internal RegularSprocketPitchDescriptor(int teeth, Rational pitch)
    {
        if (!PitchChainGeometry.SupportedTeeth(teeth) || pitch <= 0) throw new ArgumentException("Supported even tooth count and positive pin pitch required.");
        ToothCount = teeth; PitchMm = pitch; ChordLengthSquaredMm = pitch * pitch;
        PolygonContourLengthMm = teeth * pitch;
        foreach (var value in new[] { pitch, ChordLengthSquaredMm, PolygonContourLengthMm }) MechanicalDerivedNumbers.Check(value);
        DescriptorId = HashText(CanonicalRepresentation);
    }
    public string DescriptorId { get; }
    public string Semantics => PitchChainProfile.GeometrySemantics;
    public int ToothCount { get; }
    public Rational PitchMm { get; }
    public Rational ChordLengthSquaredMm { get; }
    public Rational PolygonContourLengthMm { get; }
    public string RadiusRecipe => "pitch-divided-by-two-sin-pi-over-teeth";
    public string RadiusBranch => "positive";
    public string CanonicalRepresentation => Pack(Semantics, N(ToothCount), F(PitchMm), F(ChordLengthSquaredMm),
        F(PolygonContourLengthMm), RadiusRecipe, RadiusBranch);
}

/// <summary>A bounded construction theorem with exact current premises, not a sampled trigonometric equality.</summary>
public sealed class PitchChainExactProof
{
    internal PitchChainExactProof(string id, string scope, string theorem, bool predicate,
        IEnumerable<MechanicalExactFact> facts, string detail)
    {
        Id = id; Scope = scope; Theorem = theorem; Detail = detail;
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        foreach (var f in Facts)
        {
            if (f.Expected.HasValue) MechanicalDerivedNumbers.Check(f.Expected.Value);
            if (f.Actual.HasValue) MechanicalDerivedNumbers.Check(f.Actual.Value);
        }
        IsVerified = predicate && Facts.All(f => !f.Expected.HasValue || f.Actual == f.Expected);
        ProofId = HashText(CanonicalRepresentation);
    }
    public string Id { get; }
    public string Scope { get; }
    public string Theorem { get; }
    public bool IsVerified { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public string Detail { get; }
    public string ProofId { get; }
    public string CanonicalRepresentation => Pack(Id, Scope, Theorem, PitchChainProfile.Flag(IsVerified),
        Pack(Facts.Select(f => Pack(f.Key, f.Expected.HasValue ? F(f.Expected.Value) : "", f.Actual.HasValue ? F(f.Actual.Value) : "")).ToArray()), Detail);
}

/// <summary>Fixed equal-polygon geometry; no selected chain or requested output owns this identity.</summary>
public sealed class PitchChainGeometryDescriptor
{
    internal PitchChainGeometryDescriptor(ExactVector3 c1, ExactVector3 c2, ExactVector3 normal,
        ExactVector3 e, int teeth, Rational pitch, int centerPitchCount)
    {
        InputCenterMm = c1; OutputCenterMm = c2; Normal = normal; CenterDirection = e; SideDirection = normal.Cross(e);
        Pitch = new RegularSprocketPitchDescriptor(teeth, pitch); CenterPitchCount = centerPitchCount;
        CenterDistanceMm = centerPitchCount * pitch; RequiredLinkCount = checked(2 * centerPitchCount + teeth);
        ContourLengthMm = RequiredLinkCount * pitch;
        foreach (var v in new[] { c1, c2, normal, e, SideDirection }) PitchChainGeometry.Bound(v);
        MechanicalDerivedNumbers.Check(CenterDistanceMm); MechanicalDerivedNumbers.Check(ContourLengthMm);
        GeometryId = HashText(CanonicalRepresentation);
        var h = teeth / 2;
        Proofs = new[]
        {
            new PitchChainExactProof("regular-chord", GeometryId, "regular-polygon-chord-v1", true,
                new[] { new MechanicalExactFact("chordSquared.mm2", pitch * pitch, Pitch.ChordLengthSquaredMm), new MechanicalExactFact("positivePitch.mm", null, pitch), new MechanicalExactFact("evenTeeth", null, teeth) },
                "The positive radius is p/(2 sin(pi/Z)); every adjacent tooth chord has squared length 4R^2 sin^2(pi/Z)=p^2. This is the defining regular-polygon identity, not a double residual."),
            new PitchChainExactProof("translated-support", GeometryId, "equal-polygon-translation-v1", c2 == c1 + e * CenterDistanceMm && normal.Dot(e) == 0 && SideDirection.IsCardinal,
                new[] { new MechanicalExactFact("centerDistance.mm", centerPitchCount * pitch, CenterDistanceMm), new MechanicalExactFact("straightRunLinks", centerPitchCount, centerPitchCount) },
                "Equal polygon phases modulo integer tooth registration have equal local support offsets; translated support endpoints differ by D*e at every phase."),
            new PitchChainExactProof("closed-coverage", GeometryId, "four-half-open-pin-ranges-v1", RequiredLinkCount <= PitchChainProfile.MaxLinks && RequiredLinkCount % 2 == 0,
                new[] { new MechanicalExactFact("links", RequiredLinkCount, centerPitchCount + h + centerPitchCount + h), new MechanicalExactFact("contourLength.mm", 2 * CenterDistanceMm + teeth * pitch, ContourLengthMm) },
                "M upper starts, Z/2 output starts, M lower starts and Z/2 input starts cover N distinct pins; link N-1 joins pin0 by the final polygon chord. Endpoint ownership is half-open."),
            new PitchChainExactProof("separation", GeometryId, "sin-concavity-separation-v1", centerPitchCount >= h + 1,
                new[] { new MechanicalExactFact("centerPitchCount", null, centerPitchCount), new MechanicalExactFact("sufficientMinimumM", null, h + 1) },
                "sin(pi/Z)>=2/Z gives 2R<=Z*p/2; D=M*p with M>=Z/2+1 is strictly greater than 2R. Outside this sufficient domain is unsupported, not a collision verdict."),
            new PitchChainExactProof("simple-convex-boundary", GeometryId, "translated-convex-polygon-boundary-v1", centerPitchCount > 0 && teeth % 2 == 0,
                Array.Empty<MechanicalExactFact>(), "The clockwise route is the boundary of K+[0,D]e, for centrally symmetric regular polygon K. Collinear tie edges remain consecutive pitch subdivisions, not overlapping links.")
        }.OrderBy(p => p.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string GeometryId { get; }
    public string Semantics => PitchChainProfile.GeometrySemantics;
    public RegularSprocketPitchDescriptor Pitch { get; }
    public ExactVector3 InputCenterMm { get; }
    public ExactVector3 OutputCenterMm { get; }
    public ExactVector3 Normal { get; }
    public ExactVector3 CenterDirection { get; }
    public ExactVector3 SideDirection { get; }
    public Rational CenterDistanceMm { get; }
    public int CenterPitchCount { get; }
    public int RequiredLinkCount { get; }
    public Rational ContourLengthMm { get; }
    public string Traversal => "upper-positive-e>output-decreasing-tooth>lower-negative-e>input-decreasing-tooth";
    public ReadOnlyCollection<PitchChainExactProof> Proofs { get; }
    public bool HasExactProof => Proofs.Count == 5 && Proofs.All(p => p.IsVerified);
    public string CanonicalRepresentation => Pack(Semantics, Pitch.CanonicalRepresentation, V(InputCenterMm), V(OutputCenterMm),
        V(Normal), V(CenterDirection), V(SideDirection), F(CenterDistanceMm), N(CenterPitchCount), N(RequiredLinkCount), F(ContourLengthMm), Traversal);
}

public sealed class PitchChainPhaseRegistration
{
    internal PitchChainPhaseRegistration(int teeth, Rational gamma1, Rational gamma2, Rational eps1, Rational eps2,
        Rational reference1, Rational reference2, BigInteger h)
    {
        InputGammaTurns = gamma1; OutputGammaTurns = gamma2; InputAxisRouteSign = eps1; OutputAxisRouteSign = eps2;
        InputReferenceTurns = reference1; OutputReferenceTurns = reference2; ToothRegistration = h;
        InputPhysicalReferenceTurns = gamma1 + eps1 * reference1; OutputPhysicalReferenceTurns = gamma2 + eps2 * reference2;
        RequiredPhysicalDifferenceTurns = new Rational(h, teeth);
        ResidualTurns = OutputPhysicalReferenceTurns - InputPhysicalReferenceTurns - RequiredPhysicalDifferenceTurns;
        foreach (var value in new[] { gamma1, gamma2, reference1, reference2, InputPhysicalReferenceTurns, OutputPhysicalReferenceTurns, RequiredPhysicalDifferenceTurns, ResidualTurns }) MechanicalDerivedNumbers.Check(value);
        RegistrationId = HashText(CanonicalRepresentation);
        Proof = new PitchChainExactProof("phase-registration", RegistrationId, "integer-tooth-relabeling-v1", ResidualTurns.IsZero,
            new[] { new MechanicalExactFact("phaseResidual.turn", 0, ResidualTurns), new MechanicalExactFact("physicalReferenceDifference.turn", RequiredPhysicalDifferenceTurns, OutputPhysicalReferenceTurns - InputPhysicalReferenceTurns) },
            "phi2-phi1=H/Z makes output tooth j-H exactly the translated input tooth j. The synchronized shaft edge preserves this difference at every input; references and mounting phases are not adjusted.");
    }
    public string RegistrationId { get; }
    public Rational InputGammaTurns { get; }
    public Rational OutputGammaTurns { get; }
    public Rational InputAxisRouteSign { get; }
    public Rational OutputAxisRouteSign { get; }
    public Rational InputReferenceTurns { get; }
    public Rational OutputReferenceTurns { get; }
    public Rational InputPhysicalReferenceTurns { get; }
    public Rational OutputPhysicalReferenceTurns { get; }
    public Rational RequiredPhysicalDifferenceTurns { get; }
    public BigInteger ToothRegistration { get; }
    public Rational ResidualTurns { get; }
    public PitchChainExactProof Proof { get; }
    public bool IsCompatible => Proof.IsVerified;
    public string CanonicalRepresentation => Pack(F(InputGammaTurns), F(OutputGammaTurns), F(InputAxisRouteSign), F(OutputAxisRouteSign),
        F(InputReferenceTurns), F(OutputReferenceTurns), F(InputPhysicalReferenceTurns), F(OutputPhysicalReferenceTurns),
        F(RequiredPhysicalDifferenceTurns), PitchChainProfile.I(ToothRegistration), F(ResidualTurns));
}

public enum PitchChainRouteSection { UpperStraight, OutputPolygon, LowerStraight, InputPolygon }

/// <summary>C + integer*p*e + R*(cos(2*pi*a)*e+sin(2*pi*a)*f), with exact reduced a and the typed positive R recipe.</summary>
public sealed class PitchChainPointRecipe
{
    internal PitchChainPointRecipe(RegularSprocketPitchDescriptor pitch, ExactVector3 center, ExactVector3 e, ExactVector3 f,
        Rational angle, int pitchOffset)
    {
        Pitch = pitch; CenterMm = center; CenterDirection = e; SideDirection = f;
        AngleTurns = PitchChainGeometry.ReduceTurns(angle); PitchOffset = pitchOffset;
        MechanicalDerivedNumbers.Check(AngleTurns); RecipeId = HashText(CanonicalRepresentation);
    }
    public string RecipeId { get; }
    public RegularSprocketPitchDescriptor Pitch { get; }
    public ExactVector3 CenterMm { get; }
    public ExactVector3 CenterDirection { get; }
    public ExactVector3 SideDirection { get; }
    public Rational AngleTurns { get; }
    public int PitchOffset { get; }
    public string CanonicalRepresentation => Pack(Pitch.DescriptorId, V(CenterMm), V(CenterDirection), V(SideDirection), F(AngleTurns), N(PitchOffset));
}

public sealed class PitchChainPinPose
{
    internal PitchChainPinPose(string id, int material, int route, PitchChainRouteSection section, string sprocket, int tooth, PitchChainPointRecipe position)
    { PinId = id; MaterialIndex = material; RouteIndex = route; Section = section; SprocketBodyId = sprocket; ToothIndex = tooth; Position = position; }
    public string PinId { get; }
    public int MaterialIndex { get; }
    public int RouteIndex { get; }
    public PitchChainRouteSection Section { get; }
    /// <summary>Construction host; a straight-run pin is not thereby asserted to be a tooth contact.</summary>
    public string SprocketBodyId { get; }
    public int ToothIndex { get; }
    /// <summary>Explicit polygon-piece ownership. False does not exclude support or tie coincidence with a tooth vertex.</summary>
    public bool IsPolygonVertex => Section == PitchChainRouteSection.OutputPolygon || Section == PitchChainRouteSection.InputPolygon;
    public PitchChainPointRecipe Position { get; }
    public string CanonicalRepresentation => Pack(PinId, N(MaterialIndex), N(RouteIndex), Section.ToString(), SprocketBodyId, N(ToothIndex), Position.CanonicalRepresentation);
}

public sealed class PitchChainLinkPose
{
    internal PitchChainLinkPose(string id, int material, string start, string end, Rational pitchSquared, string proofKind)
    { LinkId = id; MaterialIndex = material; StartPinId = start; EndPinId = end; LengthSquaredMm = pitchSquared; ProofKind = proofKind; }
    public string LinkId { get; }
    public int MaterialIndex { get; }
    public string StartPinId { get; }
    public string EndPinId { get; }
    public string Alternation => MaterialIndex % 2 == 0 ? "inner" : "outer";
    public Rational LengthSquaredMm { get; }
    public string ProofKind { get; }
    public string CanonicalRepresentation => Pack(LinkId, N(MaterialIndex), StartPinId, EndPinId, Alternation, F(LengthSquaredMm), ProofKind);
}

/// <summary>Absolute, stateless, unwrapped material pose. Trigonometric recipes are exact inputs, not approximate coordinates.</summary>
public sealed class IndexedChainPoseDescriptor
{
    internal IndexedChainPoseDescriptor(PitchChainGeometryDescriptor geometry, PitchChainPhaseRegistration phase,
        PitchChainSpecification selected, Rational phi, Rational outputPhi, BigInteger j, BigInteger jReference,
        Rational residual, IEnumerable<PitchChainPinPose> pins, IEnumerable<PitchChainLinkPose> links,
        IEnumerable<PitchChainExactProof> proofs)
    {
        Geometry = geometry; PhaseRegistrationId = phase.RegistrationId; SelectedSpecificationId = selected.SpecificationId;
        ChainId = selected.ChainId; MaterialRegistration = selected.MaterialRegistration;
        InputPhysicalPhaseTurns = phi; OutputPhysicalPhaseTurns = outputPhi; ReferencePhysicalPhaseTurns = phase.InputPhysicalReferenceTurns;
        UnwrappedSupportIndex = j; ReferenceSupportIndex = jReference; SupportResidualTurns = residual;
        MaterialIndexAdvance = j - jReference; MaterialRouteShift = PitchChainGeometry.Modulo(selected.MaterialRegistration + MaterialIndexAdvance, selected.LinkCount);
        MaterialRegistrationId = PitchChainGeometry.MaterialRegistrationKey(geometry, phase, selected);
        Pins = pins.ToList().AsReadOnly(); Links = links.ToList().AsReadOnly(); Proofs = proofs.OrderBy(p => p.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        ChainCirculationPhysicalTurns = new Rational(selected.LinkCount, geometry.Pitch.ToothCount);
        FullLabeledAssemblyPeriodPhysicalTurns = new Rational(selected.LinkCount / BigInteger.GreatestCommonDivisor(selected.LinkCount, geometry.Pitch.ToothCount));
        PoseId = HashText(CanonicalRepresentation);
    }
    public string PoseId { get; }
    public string Semantics => PitchChainProfile.PoseSemantics;
    public PitchChainGeometryDescriptor Geometry { get; }
    public string PhaseRegistrationId { get; }
    public string SelectedSpecificationId { get; }
    public string ChainId { get; }
    public string MaterialRegistrationId { get; }
    public BigInteger MaterialRegistration { get; }
    public Rational InputPhysicalPhaseTurns { get; }
    public Rational OutputPhysicalPhaseTurns { get; }
    public Rational ReferencePhysicalPhaseTurns { get; }
    public BigInteger UnwrappedSupportIndex { get; }
    public BigInteger ReferenceSupportIndex { get; }
    public BigInteger MaterialIndexAdvance { get; }
    public int MaterialRouteShift { get; }
    public Rational SupportResidualTurns { get; }
    public Rational ChainCirculationPhysicalTurns { get; }
    public Rational FullLabeledAssemblyPeriodPhysicalTurns { get; }
    public ReadOnlyCollection<PitchChainPinPose> Pins { get; }
    public ReadOnlyCollection<PitchChainLinkPose> Links { get; }
    public ReadOnlyCollection<PitchChainExactProof> Proofs { get; }
    public bool HasExactProof => Geometry.HasExactProof && Pins.Count == Geometry.RequiredLinkCount && Links.Count == Pins.Count && Proofs.All(p => p.IsVerified);
    public string CanonicalRepresentation => Pack(Semantics, Geometry.GeometryId, PhaseRegistrationId, SelectedSpecificationId, ChainId,
        MaterialRegistrationId, PitchChainProfile.I(MaterialRegistration), F(InputPhysicalPhaseTurns), F(OutputPhysicalPhaseTurns), F(ReferencePhysicalPhaseTurns),
        PitchChainProfile.I(UnwrappedSupportIndex), PitchChainProfile.I(ReferenceSupportIndex), PitchChainProfile.I(MaterialIndexAdvance), N(MaterialRouteShift),
        F(SupportResidualTurns), F(ChainCirculationPhysicalTurns), F(FullLabeledAssemblyPeriodPhysicalTurns),
        Pack(Pins.Select(p => p.CanonicalRepresentation).ToArray()), Pack(Links.Select(p => p.CanonicalRepresentation).ToArray()), Pack(Proofs.Select(p => p.CanonicalRepresentation).ToArray()));
}

public static class PitchChainGeometry
{
    public static bool SupportedTeeth(int teeth) => teeth >= PitchChainProfile.MinTeeth && teeth <= PitchChainProfile.MaxTeeth && teeth % 2 == 0;
    /// <summary>Mathematical floor, including negative rational values.</summary>
    public static BigInteger Floor(Rational value)
    { var q = BigInteger.DivRem(value.Numerator, value.Denominator, out var r); return r.Sign < 0 ? q - 1 : q; }
    public static Rational ReduceTurns(Rational value) => value - new Rational(Floor(value));
    public static BigInteger SupportIndex(int teeth, Rational physicalPhase)
    {
        if (!SupportedTeeth(teeth)) throw new ArgumentOutOfRangeException(nameof(teeth));
        MechanicalDerivedNumbers.Check(physicalPhase);
        return Floor(teeth * (new Rational(1, 4) - physicalPhase) + new Rational(1, 2));
    }
    public static int Modulo(BigInteger value, int count)
    { if (count <= 0 || count > PitchChainProfile.MaxLinks) throw new ArgumentOutOfRangeException(nameof(count)); var r = value % count; return (int)(r.Sign < 0 ? r + count : r); }
    internal static void Bound(ExactVector3 value)
    { MechanicalDerivedNumbers.Check(value.X); MechanicalDerivedNumbers.Check(value.Y); MechanicalDerivedNumbers.Check(value.Z); }
    internal static string MaterialRegistrationKey(PitchChainGeometryDescriptor geometry, PitchChainPhaseRegistration phase, PitchChainSpecification selected) =>
        HashText(Pack(geometry.GeometryId, selected.SpecificationId, F(phase.InputPhysicalReferenceTurns), F(phase.OutputPhysicalReferenceTurns),
            PitchChainProfile.I(phase.ToothRegistration), PitchChainProfile.I(SupportIndex(geometry.Pitch.ToothCount, phase.InputPhysicalReferenceTurns)),
            PitchChainProfile.I(selected.MaterialRegistration)));
    internal static Rational QuarterTurn(ExactVector3 zeroRay, ExactVector3 e, ExactVector3 f)
    {
        if (zeroRay == e) return 0; if (zeroRay == f) return new Rational(1, 4);
        if (zeroRay == -e) return new Rational(1, 2); if (zeroRay == -f) return new Rational(3, 4);
        throw new ArgumentException("Proper cardinal zero ray in the route plane is required.");
    }
    /// <summary>A new immutable proposal only. No draft, placement, phase or existing selected chain is changed.</summary>
    public static PitchChainSpecification CreateChainForCurrentAssembly(PitchChainDraft draft, string chainId, BigInteger materialRegistration = default)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var local = PitchChainConnectionQuery.Query(draft);
        if (local.Geometry?.HasExactProof != true) throw new ArgumentException("Current supported geometry is required for a chain-count proposal.");
        return new PitchChainSpecification(chainId, ExactQuantity.FromCanonical(QuantityKind.LinearPosition, local.Geometry.Pitch.PitchMm), local.Geometry.RequiredLinkCount, materialRegistration);
    }
    internal static IndexedChainPoseDescriptor CreatePose(PitchChainDraft draft, PitchChainCompatibilityResult local, Rational inputTurns, Rational outputTurns) =>
        CreatePose(draft.Definition.Device, local, inputTurns, outputTurns);

    internal static IndexedChainPoseDescriptor CreatePose(PitchChainTransmissionDefinition device, PitchChainCompatibilityResult local, Rational inputTurns, Rational outputTurns)
    {
        if (!local.IsAdmitted || local.Geometry is null || local.PhaseRegistration is null || device.SelectedChain is null)
            throw new ArgumentException("Only a currently admitted chain supplies material poses.");
        var g = local.Geometry; var phase = local.PhaseRegistration; var c = device; var selected = c.SelectedChain;
        var z = g.Pitch.ToothCount; var half = z / 2; var m = g.CenterPitchCount; var n = g.RequiredLinkCount;
        var phi = phase.InputGammaTurns + phase.InputAxisRouteSign * inputTurns;
        var phi2 = phase.OutputGammaTurns + phase.OutputAxisRouteSign * outputTurns;
        var j = SupportIndex(z, phi); var jr = SupportIndex(z, phase.InputPhysicalReferenceTurns);
        var residual = phi + new Rational(j, z) - new Rational(1, 4);
        foreach (var value in new[] { phi, phi2, residual }) MechanicalDerivedNumbers.Check(value);
        var shift = Modulo(selected.MaterialRegistration + j - jr, n);
        var pins = new List<PitchChainPinPose>(n); var links = new List<PitchChainLinkPose>(n);
        for (var i = 0; i < n; i++)
        {
            var k = (i + shift) % n; var offset = 0; BigInteger tooth; ExactVector3 center; string host; PitchChainRouteSection section;
            if (k < m) { section = PitchChainRouteSection.UpperStraight; center = g.InputCenterMm; tooth = j; offset = k; host = c.InputSprocketBodyId; }
            else if (k < m + half) { section = PitchChainRouteSection.OutputPolygon; center = g.OutputCenterMm; tooth = j - phase.ToothRegistration - (k - m); host = c.OutputSprocketBodyId; }
            else if (k < 2 * m + half) { section = PitchChainRouteSection.LowerStraight; center = g.InputCenterMm; tooth = j - half; offset = m - (k - m - half); host = c.InputSprocketBodyId; }
            else { section = PitchChainRouteSection.InputPolygon; center = g.InputCenterMm; tooth = j - half - (k - 2 * m - half); host = c.InputSprocketBodyId; }
            var angle = (section == PitchChainRouteSection.OutputPolygon ? phi2 : phi) + new Rational(tooth, z);
            pins.Add(new PitchChainPinPose(selected.PinId(i), i, k, section, host, Modulo(tooth, z),
                new PitchChainPointRecipe(g.Pitch, center, g.CenterDirection, g.SideDirection, angle, offset)));
        }
        // Inspect the actual endpoint recipes, including each ownership boundary and N-1 -> 0.
        // This small device-local identity checker is not a general trigonometric equality engine.
        for (var i = 0; i < n; i++)
        {
            var proofKind = VerifyAdjacentPitch(pins[i].Position, pins[(i + 1) % n].Position);
            if (proofKind is null) throw new InvalidOperationException("Current adjacent pin recipes do not satisfy an exact pitch identity.");
            links.Add(new PitchChainLinkPose(selected.LinkId(i), i, selected.PinId(i), selected.PinId((i + 1) % n), g.Pitch.ChordLengthSquaredMm, proofKind));
        }
        var proofs = new[]
        {
            new PitchChainExactProof("current-phase", phase.RegistrationId, "synchronized-tooth-position-v1", true,
                new[] { new MechanicalExactFact("phaseDifference.turn", new Rational(phase.ToothRegistration, z), phi2 - phi) }, "Current exact shaft phases preserve the declared integer tooth registration."),
            new PitchChainExactProof("half-open-support", g.GeometryId, "nearest-upper-floor-v1", residual > new Rational(-1, 2 * z) && residual <= new Rational(1, 2 * z),
                new[] { new MechanicalExactFact("supportResidual.turn", null, residual), new MechanicalExactFact("unwrappedSupportIndex", null, new Rational(j)) }, "Exact rational floor gives -1/(2Z)<r<=1/(2Z), including negative and arbitrarily large supported roots."),
            new PitchChainExactProof("material-boundary-continuity", g.GeometryId, "tie-route-shift-cancellation-v1", phase.IsCompatible && m > 0 && z % 2 == 0,
                new[] { new MechanicalExactFact("supportShiftAtIncreasingTie.mm", g.Pitch.PitchMm, g.Pitch.PitchMm), new MechanicalExactFact("sectorIndexShift", -1, -1), new MechanicalExactFact("materialPlusRouteShift", 0, -1 + 1) },
                "At either tie representation Unew=Uold+p*e and Pnew[k]=Pold[k+1]. jnew=jold-1 cancels that reindexing for every material pin. Equality is a polygon chord/translation identity, not sampled coordinates."),
            new PitchChainExactProof("material-coverage", selected.SpecificationId, "cyclic-permutation-pins-links-v1", pins.Select(p => p.RouteIndex).Distinct().Count() == n && links.Last().EndPinId == pins[0].PinId,
                new[] { new MechanicalExactFact("pinCount", n, pins.Count), new MechanicalExactFact("linkCount", n, links.Count) },
                "Modulo by N is a permutation; each persistent link joins successive persistent material pins, including the last-to-first link. Alternation follows material identity."),
            new PitchChainExactProof("all-current-link-recipes", g.GeometryId, "bounded-translated-or-adjacent-chord-v1", links.Count == n,
                new[] { new MechanicalExactFact("verifiedAdjacentRecipePairs", n, links.Count), new MechanicalExactFact("eachLinkSquared.mm2", g.Pitch.PitchMm * g.Pitch.PitchMm, g.Pitch.ChordLengthSquaredMm) },
                "Every actual consecutive endpoint recipe was checked: equal reduced angles with an exact plus/minus-p*e center difference, or equal rational centers with a clockwise adjacent tooth angle. The last-to-first pair is checked by the same rule.")
        };
        return new IndexedChainPoseDescriptor(g, phase, selected, phi, phi2, j, jr, residual, pins, links, proofs);
    }
    private static string? VerifyAdjacentPitch(PitchChainPointRecipe a, PitchChainPointRecipe b)
    {
        if (a.Pitch.DescriptorId != b.Pitch.DescriptorId || a.CenterDirection != b.CenterDirection || a.SideDirection != b.SideDirection) return null;
        var pa = a.CenterMm + a.CenterDirection * (a.PitchOffset * a.Pitch.PitchMm);
        var pb = b.CenterMm + b.CenterDirection * (b.PitchOffset * b.Pitch.PitchMm);
        var step = a.CenterDirection * a.Pitch.PitchMm;
        if (a.AngleTurns == b.AngleTurns && (pb - pa == step || pb - pa == -step)) return "exact-pitch-translation";
        if (pa == pb && ReduceTurns(b.AngleTurns - a.AngleTurns) == new Rational(a.Pitch.ToothCount - 1, a.Pitch.ToothCount))
            return "regular-polygon-adjacent-chord";
        return null;
    }
}

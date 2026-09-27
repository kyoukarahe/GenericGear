using System;
using System.Numerics;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>One rational mm term plus a signed nonnegative square root; not a general expression algebra.</summary>
public sealed class CrankSliderRadicalLength
{
    public CrankSliderRadicalLength(Rational rationalPartMm, int rootSign, Rational radicandMmSquared)
    {
        MechanicalDerivedNumbers.Check(rationalPartMm); MechanicalDerivedNumbers.Check(radicandMmSquared);
        if (rootSign is not -1 and not 0 and not 1 || radicandMmSquared < 0) throw new ArgumentException("A signed nonnegative single radical is required.");
        if (CrankSliderGeometry.TrySquareRoot(radicandMmSquared, out var root))
        { rationalPartMm += rootSign * root; rootSign = 0; radicandMmSquared = 0; MechanicalDerivedNumbers.Check(rationalPartMm); }
        if (rootSign == 0) radicandMmSquared = 0;
        RationalPartMm = rationalPartMm; RootSign = rootSign; RadicandMmSquared = radicandMmSquared;
    }
    public Rational RationalPartMm { get; } public int RootSign { get; } public Rational RadicandMmSquared { get; }
    public bool IsRational => RootSign == 0;
    public Rational? ExactMillimeters => IsRational ? RationalPartMm : null;
    public string CanonicalRepresentation => Pack(F(RationalPartMm), N(RootSign), F(RadicandMmSquared));
    public int CompareTo(Rational value)
    {
        MechanicalDerivedNumbers.Check(value); var delta = value - RationalPartMm; MechanicalDerivedNumbers.Check(delta);
        if (RootSign == 0) return RationalPartMm.CompareTo(value);
        if (RootSign < 0) return -new CrankSliderRadicalLength(-RationalPartMm, 1, RadicandMmSquared).CompareTo(-value);
        if (delta < 0) return 1;
        var square = delta * delta; MechanicalDerivedNumbers.Check(square); return RadicandMmSquared.CompareTo(square);
    }
    public CrankSliderRadicalLength Transform(int sign, Rational datum)
    {
        if (sign is not -1 and not 1) throw new ArgumentException("Terminal sign must be +1 or -1.");
        return new(sign * RationalPartMm + datum, sign * RootSign, RadicandMmSquared);
    }
}

/// <summary>Exact attained branch range, independent of source speed, travel declaration and numeric precision.</summary>
public sealed class CrankSliderEnvelope
{
    internal CrankSliderEnvelope(Rational r, Rational l, Rational d, Rational e, int branch)
    {
        StrictMarginMm = l - r - CrankSliderGeometry.Abs(e);
        if (r <= 0 || l <= 0 || StrictMarginMm <= 0 || branch is not -1 and not 1) throw new ArgumentException("Strict full-cycle geometry required.");
        MinimumRadicandMmSquared = l * l - (r + CrankSliderGeometry.Abs(e)) * (r + CrankSliderGeometry.Abs(e));
        AminusMmSquared = (l - r) * (l - r) - e * e; AplusMmSquared = (l + r) * (l + r) - e * e;
        foreach (var value in new[] { StrictMarginMm, MinimumRadicandMmSquared, AminusMmSquared, AplusMmSquared }) MechanicalDerivedNumbers.Check(value);
        Lower = new(-d, branch, branch > 0 ? AminusMmSquared : AplusMmSquared);
        Upper = new(-d, branch, branch > 0 ? AplusMmSquared : AminusMmSquared);
        if (CrankSliderGeometry.TrySquareRoot(AminusMmSquared, out var a) && CrankSliderGeometry.TrySquareRoot(AplusMmSquared, out var b)) ExactStrokeMm = b - a;
    }
    public CrankSliderRadicalLength Lower { get; } public CrankSliderRadicalLength Upper { get; }
    public Rational StrictMarginMm { get; } public Rational MinimumRadicandMmSquared { get; }
    public Rational AminusMmSquared { get; } public Rational AplusMmSquared { get; } public Rational? ExactStrokeMm { get; }
    public string StrokeRecipe => "sqrt(AplusMmSquared)-sqrt(AminusMmSquared)";
    public string AttainmentProof => "TriangleBoundsAndCollinearEndpointConstruction-v1";
    public bool Covers(ExactQuantityInterval travel) => travel.Kind == QuantityKind.LinearPosition && Lower.CompareTo(travel.Lower.Value) >= 0 && Upper.CompareTo(travel.Upper.Value) <= 0;
    public int CompareStroke(Rational requestedMm)
    {
        MechanicalDerivedNumbers.Check(requestedMm);
        if (requestedMm <= 0) return 1;
        var c = AplusMmSquared - AminusMmSquared - requestedMm * requestedMm; MechanicalDerivedNumbers.Check(c);
        if (c < 0) return -1;
        var left = c * c; var right = 4 * requestedMm * requestedMm * AminusMmSquared;
        MechanicalDerivedNumbers.Check(left); MechanicalDerivedNumbers.Check(right); return left.CompareTo(right);
    }
    public string CanonicalRepresentation => Pack(Lower.CanonicalRepresentation, Upper.CanonicalRepresentation, F(StrictMarginMm),
        F(MinimumRadicandMmSquared), F(AminusMmSquared), F(AplusMmSquared), ExactStrokeMm.HasValue ? F(ExactStrokeMm.Value) : "", AttainmentProof);
}

public sealed class CrankSliderMotionDescriptor
{
    internal CrankSliderMotionDescriptor(PlanarCrankSliderDefinition device, PrismaticOutputDefinition output,
        OrientedFrame mappedShaftFrame, ExactAffineRelation sourceRelation)
    {
        CrankRadiusMm = device.CrankRadius.Value; RodLengthMm = device.RodLength.Value;
        PivotMm = device.PivotMm; GuideOriginMm = device.GuideFrameMm.Origin; GuideDirection = device.GuideFrameMm.Z;
        PlaneNormal = device.PlaneNormal; InPlanePerpendicular = PlaneNormal.Cross(GuideDirection);
        SliderOrientation = new(ExactVector3.Zero, device.GuideFrameMm.X, device.GuideFrameMm.Y, device.GuideFrameMm.Z);
        GuideDatumMm = (GuideOriginMm - PivotMm).Dot(GuideDirection); GuideOffsetMm = (GuideOriginMm - PivotMm).Dot(InPlanePerpendicular);
        Branch = device.AssemblyBranch!.Value; TerminalSign = output.TerminalSign; TerminalDatumMm = output.TerminalDatum.Value;
        SourceRelation = sourceRelation; Epsilon = mappedShaftFrame.Z.Dot(PlaneNormal);
        GammaTurns = PitchChainGeometry.QuarterTurn(mappedShaftFrame.X, GuideDirection, InPlanePerpendicular) + Epsilon * device.MountingTurns.Value;
        PhysicalPhaseRelation = sourceRelation.Then(Epsilon, GammaTurns);
        foreach (var value in new[] { GuideDatumMm, GuideOffsetMm, GammaTurns, PhysicalPhaseRelation.Coefficient, PhysicalPhaseRelation.Phase }) MechanicalDerivedNumbers.Check(value);
        Envelope = new(CrankRadiusMm, RodLengthMm, GuideDatumMm, GuideOffsetMm, Branch);
        DescriptorId = HashText(CanonicalRepresentation);
    }
    public Rational CrankRadiusMm { get; } public Rational RodLengthMm { get; } public Rational GuideDatumMm { get; } public Rational GuideOffsetMm { get; }
    public ExactVector3 PivotMm { get; } public ExactVector3 GuideOriginMm { get; } public ExactVector3 GuideDirection { get; }
    public ExactVector3 InPlanePerpendicular { get; } public ExactVector3 PlaneNormal { get; }
    /// <summary>Fixed authored slider axes; zero origin denotes orientation only, not a world position.</summary>
    public OrientedFrame SliderOrientation { get; }
    public int Branch { get; } public int TerminalSign { get; } public Rational TerminalDatumMm { get; }
    public Rational Epsilon { get; } public Rational GammaTurns { get; }
    public ExactAffineRelation SourceRelation { get; } public ExactAffineRelation PhysicalPhaseRelation { get; }
    public CrankSliderEnvelope Envelope { get; } public string DescriptorId { get; }
    public string Policy => CrankSliderProfile.AnalysisPolicy;
    public Rational? PositionRepeatRootTurns => PhysicalPhaseRelation.Coefficient.IsZero ? null : 1 / CrankSliderGeometry.Abs(PhysicalPhaseRelation.Coefficient);
    public string PositionRecipe => "r*cos(2*pi*phi)+B*sqrt(l*l-(e-r*sin(2*pi*phi))^2)-d";
    public string CanonicalRepresentation => Pack(Policy, F(CrankRadiusMm), F(RodLengthMm), F(GuideDatumMm), F(GuideOffsetMm),
        V(PivotMm), V(GuideOriginMm), V(GuideDirection), V(InPlanePerpendicular), V(PlaneNormal), Frame(SliderOrientation), N(Branch), N(TerminalSign), F(TerminalDatumMm),
        F(SourceRelation.Coefficient), F(SourceRelation.Phase), F(PhysicalPhaseRelation.Coefficient), F(PhysicalPhaseRelation.Phase), F(Epsilon), F(GammaTurns), Envelope.CanonicalRepresentation);
    public CrankSliderPoseRecipe At(ExactQuantity root) => new(this, root);
}

public sealed class CrankSliderPoseRecipe
{
    internal CrankSliderPoseRecipe(CrankSliderMotionDescriptor descriptor, ExactQuantity root)
    {
        if (root.Kind != QuantityKind.AngularPosition) throw new ArgumentException("DimensionMismatch: crank-slider input must be unwrapped turns.");
        Descriptor = descriptor; Root = root;
        SourceTurns = descriptor.SourceRelation.Evaluate(root.Value); PhysicalPhaseTurns = descriptor.PhysicalPhaseRelation.Evaluate(root.Value);
        MechanicalDerivedNumbers.Check(SourceTurns); MechanicalDerivedNumbers.Check(PhysicalPhaseTurns);
        ReducedPhaseTurns = PitchChainGeometry.ReduceTurns(PhysicalPhaseTurns); RecipeId = HashText(CanonicalRepresentation);
    }
    public CrankSliderMotionDescriptor Descriptor { get; } public ExactQuantity Root { get; }
    public Rational SourceTurns { get; } public Rational PhysicalPhaseTurns { get; } public Rational ReducedPhaseTurns { get; }
    public string RecipeId { get; }
    public OrientedFrame SliderOrientation => Descriptor.SliderOrientation;
    public string RodOrientationRecipe => "columns=((P-A)/l,n cross ((P-A)/l),n); midpoint=(A+P)/2";
    public string CanonicalRepresentation => Pack(Descriptor.DescriptorId, CrankSliderProfile.Q(Root), F(SourceTurns), F(PhysicalPhaseTurns), F(ReducedPhaseTurns), RodOrientationRecipe);
    public CrankSliderRadicalLength? ExactGuidePosition => CrankSliderGeometry.QuarterPosition(Descriptor, ReducedPhaseTurns);
    public CrankSliderRadicalLength? ExactTerminalPosition => ExactGuidePosition?.Transform(Descriptor.TerminalSign, Descriptor.TerminalDatumMm);
}

internal static class CrankSliderGeometry
{
    internal static Rational Abs(Rational value) => value < 0 ? -value : value;
    internal static bool TrySquareRoot(Rational value, out Rational root)
    {
        MechanicalDerivedNumbers.Check(value); root = 0; if (value < 0) return false;
        var a = IntegerRoot(value.Numerator); var b = IntegerRoot(value.Denominator);
        if (a * a != value.Numerator || b * b != value.Denominator) return false;
        root = new Rational(a, b); return true;
    }
    private static BigInteger IntegerRoot(BigInteger value)
    {
        if (value <= 1) return value;
        var bytes = value.ToByteArray(); var x = BigInteger.One << ((bytes.Length * 8 + 1) / 2);
        while (true) { var next = (x + value / x) / 2; if (next >= x) return x; x = next; }
    }
    internal static CrankSliderRadicalLength? QuarterPosition(CrankSliderMotionDescriptor d, Rational reducedPhase)
    {
        Rational sine, cosine;
        if (reducedPhase == 0) { sine = 0; cosine = 1; }
        else if (reducedPhase == new Rational(1, 4)) { sine = 1; cosine = 0; }
        else if (reducedPhase == new Rational(1, 2)) { sine = 0; cosine = -1; }
        else if (reducedPhase == new Rational(3, 4)) { sine = -1; cosine = 0; }
        else return null;
        var h = d.GuideOffsetMm - d.CrankRadiusMm * sine;
        return new(d.CrankRadiusMm * cosine - d.GuideDatumMm, d.Branch, d.RodLengthMm * d.RodLengthMm - h * h);
    }
}

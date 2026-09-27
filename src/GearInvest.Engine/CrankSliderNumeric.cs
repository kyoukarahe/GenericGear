using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum CrankSliderNumericStatus { ExactValue, EnclosedValue, IncompleteNumericBudget, NumericResourceLimit, InvalidNumericRequest }

/// <summary>Options remain representable when invalid, so evaluation can return an explicit request verdict.</summary>
public sealed class CrankSliderNumericRequest
{
    public const string CurrentPolicy = "crank-slider-dyadic-enclosure-v1";
    public CrankSliderNumericRequest(ExactQuantity? absoluteWidth = null, int maximumWork = 8192,
        int maximumPrecisionBits = 512, int maximumRefinements = 4, string? policy = CurrentPolicy)
    { AbsoluteWidth = absoluteWidth ?? ExactQuantity.Millimeters(new Rational(1, 1000000000)); MaximumWork = maximumWork; MaximumPrecisionBits = maximumPrecisionBits; MaximumRefinements = maximumRefinements; Policy = policy; }
    public ExactQuantity AbsoluteWidth { get; }
    public int MaximumWork { get; }
    public int MaximumPrecisionBits { get; }
    public int MaximumRefinements { get; }
    public string? Policy { get; }
    public static CrankSliderNumericRequest Default => new();
    public string CanonicalRepresentation => Pack(Policy is null ? "0" : "1", Policy ?? "", AbsoluteWidth.Kind.ToString(), AbsoluteWidth.Unit, F(AbsoluteWidth.Value), N(MaximumWork), N(MaximumPrecisionBits), N(MaximumRefinements));
}

/// <summary>Certified closed rational bounds, not an exact representation of a nonlinear value.</summary>
public sealed class CrankSliderInterval
{
    internal CrankSliderInterval(Rational lower, Rational upper)
    { MechanicalDerivedNumbers.Check(lower); MechanicalDerivedNumbers.Check(upper); if (lower > upper) throw new ArgumentException("Empty crank-slider numerical interval."); Lower = lower; Upper = upper; }
    public Rational Lower { get; }
    public Rational Upper { get; }
    public Rational Width => Upper - Lower;
    public bool IsExact => Lower == Upper;
    public string CanonicalRepresentation => Pack(F(Lower), F(Upper));
}

public sealed class CrankSliderVectorInterval
{
    internal CrankSliderVectorInterval(CrankSliderInterval x, CrankSliderInterval y, CrankSliderInterval z) { X = x; Y = y; Z = z; }
    public CrankSliderInterval X { get; }
    public CrankSliderInterval Y { get; }
    public CrankSliderInterval Z { get; }
    public string CanonicalRepresentation => Pack(X.CanonicalRepresentation, Y.CanonicalRepresentation, Z.CanonicalRepresentation);
}

/// <summary>All positions are mm. Rod direction and transverse direction are dimensionless.</summary>
public sealed class CrankSliderNumericPose
{
    internal CrankSliderNumericPose(CrankSliderInterval guide, CrankSliderInterval terminal, CrankSliderVectorInterval crank,
        CrankSliderVectorInterval slider, CrankSliderVectorInterval midpoint, CrankSliderVectorInterval direction,
        CrankSliderVectorInterval transverse, CrankSliderInterval sin, CrankSliderInterval cos, CrankSliderInterval d, CrankSliderInterval sqrt)
    { GuidePositionMm = guide; TerminalPositionMm = terminal; CrankPinMm = crank; SliderPinMm = slider; RodMidpointMm = midpoint; RodDirection = direction; RodTransverseDirection = transverse; Sin = sin; Cos = cos; RadicandMmSquared = d; PositiveRootMm = sqrt; }
    public CrankSliderInterval GuidePositionMm { get; }
    public CrankSliderInterval TerminalPositionMm { get; }
    public CrankSliderVectorInterval CrankPinMm { get; }
    public CrankSliderVectorInterval SliderPinMm { get; }
    public CrankSliderVectorInterval RodMidpointMm { get; }
    public CrankSliderVectorInterval RodDirection { get; }
    public CrankSliderVectorInterval RodTransverseDirection { get; }
    public CrankSliderInterval Sin { get; }
    public CrankSliderInterval Cos { get; }
    public CrankSliderInterval RadicandMmSquared { get; }
    public CrankSliderInterval PositiveRootMm { get; }
    public string CanonicalRepresentation => Pack(GuidePositionMm.CanonicalRepresentation, TerminalPositionMm.CanonicalRepresentation,
        CrankPinMm.CanonicalRepresentation, SliderPinMm.CanonicalRepresentation, RodMidpointMm.CanonicalRepresentation,
        RodDirection.CanonicalRepresentation, RodTransverseDirection.CanonicalRepresentation, Sin.CanonicalRepresentation,
        Cos.CanonicalRepresentation, RadicandMmSquared.CanonicalRepresentation, PositiveRootMm.CanonicalRepresentation);
}

public sealed class CrankSliderNumericComputation
{
    internal CrankSliderNumericComputation(CrankSliderNumericStatus status, int work, int bits, int refinements,
        CrankSliderNumericPose? pose, CrankSliderNumericPose? diagnostic, string detail)
    { Status = status; Work = work; PrecisionBits = bits; Refinements = refinements; Pose = pose; DiagnosticPose = diagnostic; Detail = detail; }
    public CrankSliderNumericStatus Status { get; }
    public int Work { get; }
    public int PrecisionBits { get; }
    public int Refinements { get; }
    public CrankSliderNumericPose? Pose { get; }
    public CrankSliderNumericPose? DiagnosticPose { get; }
    public string Detail { get; }
    public bool IsAvailable => Status == CrankSliderNumericStatus.ExactValue || Status == CrankSliderNumericStatus.EnclosedValue;
    public string CanonicalRepresentation => Pack(Status.ToString(), N(Work), N(PrecisionBits), N(Refinements),
        Pose?.CanonicalRepresentation ?? "none", DiagnosticPose?.CanonicalRepresentation ?? "none", Detail);
}

/// <summary>Device-local certified arithmetic. All canonical decisions use integers, never host transcendental functions.</summary>
internal static class CrankSliderNumerics
{
    internal static CrankSliderNumericComputation Evaluate(CrankSliderPoseRecipe recipe, CrankSliderNumericRequest request)
    {
        var m = recipe.Descriptor;
        return Evaluate(recipe.PhysicalPhaseTurns, m.CrankRadiusMm, m.RodLengthMm, m.GuideDatumMm, m.GuideOffsetMm,
            m.Branch, m.PivotMm, m.GuideOriginMm, m.GuideDirection, m.InPlanePerpendicular, m.PlaneNormal, m.TerminalSign, m.TerminalDatumMm, request);
    }
    private static readonly int[] Precisions = { 64, 128, 256, 512, 1024, 2048, 4096 };
    internal static CrankSliderNumericComputation Evaluate(Rational physicalPhaseTurns, Rational radiusMm, Rational rodLengthMm,
        Rational guideDatumMm, Rational guideOffsetMm, int branch, ExactVector3 pivotMm, ExactVector3 guideOriginMm,
        ExactVector3 guideDirection, ExactVector3 inPlanePerpendicular, ExactVector3 planeNormal, int terminalSign,
        Rational terminalDatumMm, CrankSliderNumericRequest request)
    {
        if (request is null || request.Policy != CrankSliderNumericRequest.CurrentPolicy ||
            request.AbsoluteWidth.Kind != QuantityKind.LinearPosition || request.AbsoluteWidth.Value <= 0 ||
            !InputBound(request.AbsoluteWidth.Value) || request.MaximumWork < 0 || request.MaximumWork > 262144 ||
            !Precisions.Contains(request.MaximumPrecisionBits) || request.MaximumRefinements < 1 || request.MaximumRefinements > 7)
            return new(CrankSliderNumericStatus.InvalidNumericRequest, 0, 0, 0, null, null, "Invalid quantity, width, policy or bounded numerical options.");
        var c = new Context(request.MaximumWork); CrankSliderNumericPose? last = null; int bits = 0, passes = 0;
        try
        {
            foreach (var value in new[] { physicalPhaseTurns, radiusMm, rodLengthMm, guideDatumMm, guideOffsetMm, terminalDatumMm,
                pivotMm.X, pivotMm.Y, pivotMm.Z, guideOriginMm.X, guideOriginMm.Y, guideOriginMm.Z }) c.Check(value);
            if (radiusMm <= 0 || rodLengthMm <= c.Add(radiusMm, Abs(guideOffsetMm)) || (branch != 1 && branch != -1) || (terminalSign != 1 && terminalSign != -1))
                throw new ArgumentException("Certified crank-slider evaluation requires admitted strict full-cycle geometry and explicit signs.");
            c.Step();
            var rem = physicalPhaseTurns.Numerator % physicalPhaseTurns.Denominator;
            if (rem.Sign < 0) rem += physicalPhaseTurns.Denominator;
            var phase = new Rational(rem, physicalPhaseTurns.Denominator);
            var quarter = c.Multiply(phase, 4);
            var isQuarter = quarter.Denominator.IsOne;
            var l2 = c.Multiply(rodLengthMm, rodLengthMm);
            var maxH = c.Add(radiusMm, Abs(guideOffsetMm));
            var dmin = c.Subtract(l2, c.Multiply(maxH, maxH));
            var directionWidth = c.Divide(request.AbsoluteWidth.Value, Max(1, Max(radiusMm, rodLengthMm)));
            foreach (var p in Precisions)
            {
                if (p > request.MaximumPrecisionBits) break;
                if (passes == request.MaximumRefinements) throw new Stop(CrankSliderNumericStatus.IncompleteNumericBudget, "Refinement-pass budget exhausted.");
                c.Step(); bits = p; passes++; c.SetPrecision(p);
                CrankSliderInterval sin, cos;
                if (isQuarter)
                {
                    var q = (int)quarter.Numerator;
                    sin = Point(q == 1 ? 1 : q == 3 ? -1 : 0);
                    cos = Point(q == 0 ? 1 : q == 2 ? -1 : 0);
                }
                else (sin, cos) = SinCos(c, phase);
                var h = c.Subtract(Point(guideOffsetMm), c.Scale(sin, radiusMm));
                var d = c.Subtract(Point(l2), c.Square(h));
                // Proven set intersection. A contradiction is an internal error, never a repaired result.
                var lo = Max(d.Lower, dmin); var hi = Min(d.Upper, l2);
                if (lo > hi) throw new InvalidOperationException("Certified radicand interval contradicts the strict full-cycle proof.");
                d = c.Interval(lo, hi);
                var root = c.Sqrt(d, isQuarter);
                var x = c.Subtract(c.Add(c.Scale(cos, radiusMm), c.Scale(root, branch)), Point(guideDatumMm));
                var y = c.Add(c.Scale(x, terminalSign), Point(terminalDatumMm));
                var a = Vector(c, pivotMm, guideDirection, c.Scale(cos, radiusMm), inPlanePerpendicular, c.Scale(sin, radiusMm));
                var slider = Vector(c, guideOriginMm, guideDirection, x, inPlanePerpendicular, Point(0));
                var center = new CrankSliderVectorInterval(c.Scale(c.Add(a.X, slider.X), new Rational(1, 2)), c.Scale(c.Add(a.Y, slider.Y), new Rational(1, 2)), c.Scale(c.Add(a.Z, slider.Z), new Rational(1, 2)));
                var along = c.Scale(root, c.Divide(branch, rodLengthMm));
                var side = c.Scale(h, c.Divide(1, rodLengthMm));
                var v = Vector(c, ExactVector3.Zero, guideDirection, along, inPlanePerpendicular, side);
                var transverse = Vector(c, ExactVector3.Zero, planeNormal.Cross(guideDirection), along, planeNormal.Cross(inPlanePerpendicular), side);
                last = new(x, y, a, slider, center, v, transverse, sin, cos, d, root);
                var positions = new[] { x, y, a.X, a.Y, a.Z, slider.X, slider.Y, slider.Z, center.X, center.Y, center.Z };
                var directions = new[] { v.X, v.Y, v.Z, transverse.X, transverse.Y, transverse.Z };
                if (positions.All(i => c.Width(i) <= request.AbsoluteWidth.Value) && directions.All(i => c.Width(i) <= directionWidth))
                {
                    var exact = isQuarter && root.IsExact && positions.All(i => i.IsExact) && directions.All(i => i.IsExact);
                    return new(exact ? CrankSliderNumericStatus.ExactValue : CrankSliderNumericStatus.EnclosedValue, c.Work, bits, passes, last, null, "Requested scalar, position and direction enclosure widths satisfied.");
                }
            }
            return new(CrankSliderNumericStatus.NumericResourceLimit, c.Work, bits, passes, null, last, "Maximum precision reached before all requested widths were proved.");
        }
        catch (Stop stop) { return new(stop.Status, c.Work, bits, passes, null, last, stop.Message); }
    }

    internal static bool InputBound(Rational x) => x.Numerator.ToString(CultureInfo.InvariantCulture).Length <= 128 && x.Denominator.ToString(CultureInfo.InvariantCulture).Length <= 128;
    private static Rational Abs(Rational x) => x.Sign < 0 ? -x : x;
    private static Rational Min(Rational a, Rational b) => a < b ? a : b;
    private static Rational Max(Rational a, Rational b) => a > b ? a : b;
    private static CrankSliderInterval Point(Rational x) => new(x, x);
    private static CrankSliderVectorInterval Vector(Context c, ExactVector3 origin, ExactVector3 g, CrankSliderInterval u, ExactVector3 f, CrankSliderInterval v) =>
        new(c.Add(Point(origin.X), c.Add(c.Scale(u, g.X), c.Scale(v, f.X))),
            c.Add(Point(origin.Y), c.Add(c.Scale(u, g.Y), c.Scale(v, f.Y))),
            c.Add(Point(origin.Z), c.Add(c.Scale(u, g.Z), c.Scale(v, f.Z))));

    internal static (CrankSliderInterval Sin, CrankSliderInterval Cos) SinCos(Context c, Rational phase)
    {
        var quarters = c.Multiply(phase, 4); var quadrant = (int)(quarters.Numerator / quarters.Denominator);
        var residual = c.Subtract(phase, new Rational(quadrant, 4));
        bool swap = residual > new Rational(1, 8);
        if (swap) residual = c.Subtract(new Rational(1, 4), residual);
        var pi = c.GridSubtract(c.GridScale(Atan(c, 5), 16), c.GridScale(Atan(c, 239), 4));
        var angle = c.GridMultiply(pi, c.ToGrid(c.Multiply(2, residual)));
        // pi/4<1. Grid upper uncertainty remains well below1 at every allowed precision.
        if (angle.L < 0 || angle.H > c.ScaleGrid) throw new InvalidOperationException("Reduced trigonometric argument exceeded the proved Taylor domain.");
        var sin = Taylor(c, angle, true); var cos = Taylor(c, angle, false);
        if (swap) (sin, cos) = (cos, sin);
        Grid s, co;
        switch (quadrant)
        {
            case 0: s = sin; co = cos; break;
            case 1: s = cos; co = c.GridNegate(sin); break;
            case 2: s = c.GridNegate(sin); co = c.GridNegate(cos); break;
            default: s = c.GridNegate(cos); co = sin; break;
        }
        return (c.FromGrid(s), c.FromGrid(co));
    }

    internal static Grid Atan(Context c, int q)
    {
        BigInteger power = q; var sum = new Grid(0, 0);
        for (int k = 0; ; k++)
        {
            c.Step(); // One signed atan term.
            var denominator = c.Product(power, 2 * k + 1);
            var term = new Grid(c.ScaleGrid / denominator, Ceil(c.ScaleGrid, denominator));
            sum = k % 2 == 0 ? c.GridAdd(sum, term) : c.GridSubtract(sum, term);
            power = c.Product(power, q * q);
            var nextDenominator = c.Product(power, 2 * k + 3);
            if (nextDenominator >= c.ScaleGrid)
            {
                // First omitted term bounds the signed alternating tail, at most one grid unit.
                return k % 2 == 0 ? c.GridAdd(sum, new Grid(-1, 0)) : c.GridAdd(sum, new Grid(0, 1));
            }
        }
    }

    private static Grid Taylor(Context c, Grid angle, bool sine)
    {
        var square = c.GridMultiply(angle, angle); var term = sine ? angle : new Grid(c.ScaleGrid, c.ScaleGrid);
        var sum = new Grid(0, 0);
        for (int k = 0; ; k++)
        {
            c.Step(); // One signed sine/cosine Taylor term.
            sum = k % 2 == 0 ? c.GridAdd(sum, term) : c.GridSubtract(sum, term);
            var denominator = sine ? (2 * k + 2) * (2 * k + 3) : (2 * k + 1) * (2 * k + 2);
            term = c.GridDivide(c.GridMultiply(term, square), denominator);
            if (term.H <= 1)
            {
                // Decreasing magnitudes on[0,1] give an alternating remainder bounded by this term.
                return k % 2 == 0 ? c.GridAdd(sum, new Grid(-term.H, 0)) : c.GridAdd(sum, new Grid(0, term.H));
            }
        }
    }

    internal readonly struct Grid
    {
        internal Grid(BigInteger l, BigInteger h) { L = l; H = h; }
        internal BigInteger L { get; }
        internal BigInteger H { get; }
    }
    internal sealed class Stop : Exception
    {
        internal Stop(CrankSliderNumericStatus status, string detail) : base(detail) { Status = status; }
        internal CrankSliderNumericStatus Status { get; }
    }
    private static int BitLength(BigInteger value)
    {
        var bytes = BigInteger.Abs(value).ToByteArray(); var top = bytes.Length - 1;
        while (top > 0 && bytes[top] == 0) top--;
        int bits = top * 8; var b = bytes[top]; while (b > 0) { bits++; b >>= 1; } return bits;
    }
    private static BigInteger Floor(BigInteger n, BigInteger positiveD)
    { var q = BigInteger.DivRem(n, positiveD, out var r); return r.Sign < 0 ? q - 1 : q; }
    private static BigInteger Ceil(BigInteger n, BigInteger positiveD) => -Floor(-n, positiveD);

    internal sealed class Context
    {
        private readonly int maximumWork;
        internal Context(int maximumWork) { this.maximumWork = maximumWork; }
        internal int Work { get; private set; }
        internal int Precision { get; private set; }
        internal BigInteger ScaleGrid { get; private set; }
        internal void Step()
        { if (Work >= maximumWork) throw new Stop(CrankSliderNumericStatus.IncompleteNumericBudget, "Deterministic numerical work budget exhausted."); Work++; }
        internal void SetPrecision(int bits) { Precision = bits; ScaleGrid = BigInteger.One << bits; }
        internal void Check(Rational x)
        {
            if (!MechanicalDerivedNumbers.IsWithinComponentBound(x.Numerator) || !MechanicalDerivedNumbers.IsWithinComponentBound(x.Denominator))
                throw new Stop(CrankSliderNumericStatus.NumericResourceLimit, "Derived rational exceeds the existing8192-character component bound.");
        }
        private static void Bits(int bits)
        { if (bits > 65536) throw new Stop(CrankSliderNumericStatus.NumericResourceLimit, "Intermediate integer exceeds65536bits."); }
        internal BigInteger Product(BigInteger a, BigInteger b) { Bits(BitLength(a) + BitLength(b)); return a * b; }
        private BigInteger Shift(BigInteger a, int bits) { Bits(BitLength(a) + bits); return a << bits; }
        internal Rational Add(Rational a, Rational b)
        {
            Step(); var left = Product(a.Numerator, b.Denominator); var right = Product(b.Numerator, a.Denominator);
            Bits(Math.Max(BitLength(left), BitLength(right)) + 1);
            var value = new Rational(left + right, Product(a.Denominator, b.Denominator)); Check(value); return value;
        }
        internal Rational Subtract(Rational a, Rational b) => Add(a, -b);
        internal Rational Multiply(Rational a, Rational b)
        { Step(); var value = new Rational(Product(a.Numerator, b.Numerator), Product(a.Denominator, b.Denominator)); Check(value); return value; }
        internal Rational Divide(Rational a, Rational b)
        { Step(); var value = new Rational(Product(a.Numerator, b.Denominator), Product(a.Denominator, b.Numerator)); Check(value); return value; }
        internal CrankSliderInterval Interval(Rational lower, Rational upper) { Check(lower); Check(upper); return new(lower, upper); }
        internal Rational Width(CrankSliderInterval x) => Subtract(x.Upper, x.Lower);
        internal CrankSliderInterval Add(CrankSliderInterval a, CrankSliderInterval b) => Interval(Add(a.Lower, b.Lower), Add(a.Upper, b.Upper));
        internal CrankSliderInterval Subtract(CrankSliderInterval a, CrankSliderInterval b) => Interval(Subtract(a.Lower, b.Upper), Subtract(a.Upper, b.Lower));
        internal CrankSliderInterval Scale(CrankSliderInterval a, Rational b) => b.Sign >= 0 ? Interval(Multiply(a.Lower, b), Multiply(a.Upper, b)) : Interval(Multiply(a.Upper, b), Multiply(a.Lower, b));
        internal CrankSliderInterval Square(CrankSliderInterval a)
        {
            var x = Multiply(a.Lower, a.Lower); var y = Multiply(a.Upper, a.Upper);
            return Interval(a.Lower <= 0 && a.Upper >= 0 ? 0 : Min(x, y), Max(x, y));
        }
        internal Grid ToGrid(Rational x)
        { Step(); var n = Shift(x.Numerator, Precision); return new(Floor(n, x.Denominator), Ceil(n, x.Denominator)); }
        internal CrankSliderInterval FromGrid(Grid x) => Interval(new Rational(x.L, ScaleGrid), new Rational(x.H, ScaleGrid));
        internal Grid GridAdd(Grid a, Grid b)
        { Step(); Bits(Math.Max(Math.Max(BitLength(a.L), BitLength(a.H)), Math.Max(BitLength(b.L), BitLength(b.H))) + 1); return new(a.L + b.L, a.H + b.H); }
        internal Grid GridNegate(Grid a) => new(-a.H, -a.L);
        internal Grid GridSubtract(Grid a, Grid b) => GridAdd(a, GridNegate(b));
        internal Grid GridScale(Grid a, int n)
        { Step(); return n >= 0 ? new(Product(a.L, n), Product(a.H, n)) : new(Product(a.H, n), Product(a.L, n)); }
        internal Grid GridMultiply(Grid a, Grid b)
        {
            Step(); var values = new[] { Product(a.L, b.L), Product(a.L, b.H), Product(a.H, b.L), Product(a.H, b.H) };
            return new(Floor(values.Min(), ScaleGrid), Ceil(values.Max(), ScaleGrid));
        }
        internal Grid GridDivide(Grid a, int positiveDivisor)
        { Step(); return new(Floor(a.L, positiveDivisor), Ceil(a.H, positiveDivisor)); }
        private BigInteger IntegerSqrt(BigInteger n)
        {
            Step(); if (n <= 1) return n;
            var x = BigInteger.One << ((BitLength(n) + 1) / 2);
            while (true)
            {
                Step(); var y = (x + n / x) >> 1;
                if (y >= x)
                {
                    if (Product(x, x) > n || Product(x + 1, x + 1) <= n) throw new InvalidOperationException("Integer square-root postcondition failed.");
                    return x;
                }
                x = y;
            }
        }
        internal CrankSliderInterval Sqrt(CrankSliderInterval value, bool tryExact)
        {
            if (value.Lower < 0) throw new InvalidOperationException("Positive square-root received a negative proven lower endpoint.");
            if (tryExact && value.IsExact)
            {
                var n = IntegerSqrt(value.Lower.Numerator); var d = IntegerSqrt(value.Lower.Denominator);
                if (Product(n, n) == value.Lower.Numerator && Product(d, d) == value.Lower.Denominator) return Point(new Rational(n, d));
            }
            Rational Bound(Rational z, bool upper)
            {
                Step(); var scaled = Shift(z.Numerator, 2 * Precision); var k = IntegerSqrt(scaled / z.Denominator);
                var exact = Product(Product(k, k), z.Denominator) == scaled;
                return new Rational(upper && !exact ? k + 1 : k, ScaleGrid);
            }
            return Interval(Bound(value.Lower, false), Bound(value.Upper, true));
        }
    }
}

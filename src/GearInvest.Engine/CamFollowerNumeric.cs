using System;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum CamNumericStatus { ExactValue, EnclosedValue, IncompleteNumericBudget, NumericResourceLimit, InvalidNumericRequest }
public enum CamPiComparison { Less, Equal, Greater, Unresolved }

public sealed class CamNumericRequest
{
    public const string CurrentPolicy = "cam-support-dyadic-enclosure-v1";
    public CamNumericRequest(ExactQuantity? absoluteWidth = null, int maximumWork = 8192, int maximumPrecisionBits = 512,
        int maximumRefinements = 4, string? policy = CurrentPolicy)
    {
        AbsoluteWidth = absoluteWidth ?? ExactQuantity.Millimeters(new Rational(1, 1000000000));
        MaximumWork = maximumWork; MaximumPrecisionBits = maximumPrecisionBits; MaximumRefinements = maximumRefinements; Policy = policy;
    }
    public ExactQuantity AbsoluteWidth { get; }
    /// <summary>Whole-call arithmetic ceiling; follower evaluation shares it across face comparisons and both points.</summary>
    public int MaximumWork { get; }
    public int MaximumPrecisionBits { get; }
    public int MaximumRefinements { get; }
    public string? Policy { get; }
    public static CamNumericRequest Default => new();
    public string CanonicalRepresentation => Pack(Policy is null ? "0" : "1", Policy ?? "", RotaryLinearProfile.Q(AbsoluteWidth),
        N(MaximumWork), N(MaximumPrecisionBits), N(MaximumRefinements));
}

/// <summary>Certified closed rational interval; a nonzero width is not an exact value.</summary>
public sealed class CamInterval
{
    internal CamInterval(Rational lower, Rational upper)
    {
        MechanicalDerivedNumbers.Check(lower); MechanicalDerivedNumbers.Check(upper);
        if (lower > upper) throw new ArgumentException("Empty cam interval.");
        Lower = lower; Upper = upper;
    }
    internal CamInterval(CrankSliderInterval value) : this(value.Lower, value.Upper) { }
    public Rational Lower { get; }
    public Rational Upper { get; }
    public Rational Width => Upper - Lower;
    public bool IsExact => Lower == Upper;
    public string CanonicalRepresentation => Pack(F(Lower), F(Upper));
}

public sealed class CamVectorInterval
{
    internal CamVectorInterval(CrankSliderInterval x, CrankSliderInterval y, CrankSliderInterval z)
    { X = new(x); Y = new(y); Z = new(z); }
    public CamInterval X { get; }
    public CamInterval Y { get; }
    public CamInterval Z { get; }
    public string CanonicalRepresentation => Pack(X.CanonicalRepresentation, Y.CanonicalRepresentation, Z.CanonicalRepresentation);
}

/// <summary>A numerical point result, not authorization for a connected follower pose.</summary>
public sealed class CamNumericResult
{
    internal CamNumericResult(string inputId, CamNumericRequest request, CamNumericStatus status, int work, int bits,
        int refinements, CamVectorInterval? point, CamVectorInterval? diagnostic, string detail)
    {
        InputId = inputId; Request = request; Status = status; Work = work; PrecisionBits = bits; Refinements = refinements;
        Point = point; DiagnosticPoint = diagnostic; Detail = detail;
    }
    public string InputId { get; }
    public CamNumericRequest Request { get; }
    public CamNumericStatus Status { get; }
    public bool IsAvailable => Status == CamNumericStatus.ExactValue || Status == CamNumericStatus.EnclosedValue;
    public CamVectorInterval? Point { get; }
    public CamVectorInterval? DiagnosticPoint { get; }
    public int Work { get; }
    public int PrecisionBits { get; }
    public int Refinements { get; }
    public string Detail { get; }
    public string ResultId => HashText(CanonicalRepresentation);
    public string CanonicalRepresentation => Pack(InputId, Request.CanonicalRepresentation, Status.ToString(), N(Work), N(PrecisionBits),
        N(Refinements), Point?.CanonicalRepresentation ?? "none", DiagnosticPoint?.CanonicalRepresentation ?? "none", Detail);
}

/// <summary>Certified ordering evidence with its own bounded arithmetic accounting.</summary>
public sealed class CamPiComparisonResult
{
    internal CamPiComparisonResult(ExactPiLength left, ExactPiLength right, CamNumericRequest request, CamPiComparison comparison,
        CamNumericStatus status, int work, int bits, int refinements, CamInterval? interval, string detail)
    {
        Left = left; Right = right; Request = request; Comparison = comparison; Status = status; Work = work;
        PrecisionBits = bits; Refinements = refinements; PiScaledDifferenceInterval = interval; Detail = detail;
    }
    public ExactPiLength Left { get; }
    public ExactPiLength Right { get; }
    public CamNumericRequest Request { get; }
    public CamPiComparison Comparison { get; }
    public CamNumericStatus Status { get; }
    public bool IsResolved => Comparison != CamPiComparison.Unresolved;
    public int Work { get; }
    public int PrecisionBits { get; }
    public int Refinements { get; }
    /// <summary>Encloses pi*(Left-Right); pi is positive so it certifies the same ordering.</summary>
    public CamInterval? PiScaledDifferenceInterval { get; }
    public string Detail { get; }
    public string ResultId => HashText(CanonicalRepresentation);
    public string CanonicalRepresentation => Pack(F(Left.RationalPartMm), F(Left.InversePiCoefficientMm),
        F(Right.RationalPartMm), F(Right.InversePiCoefficientMm), Request.CanonicalRepresentation, Comparison.ToString(),
        Status.ToString(), N(Work), N(PrecisionBits), N(Refinements), PiScaledDifferenceInterval?.CanonicalRepresentation ?? "none", Detail);
}

/// <summary>28A calls the existing 27A certified primitive operations without changing the 27A policy or counts.</summary>
internal static class CamNumericalPrimitives
{
    internal static readonly int[] Precisions = { 64, 128, 256, 512, 1024, 2048, 4096 };
    internal static bool InputBound(Rational x) => x.Numerator.ToString(CultureInfo.InvariantCulture).Length <= 128 &&
        x.Denominator.ToString(CultureInfo.InvariantCulture).Length <= 128;
    internal static bool Valid(CamNumericRequest request) => request.Policy == CamNumericRequest.CurrentPolicy &&
        request.AbsoluteWidth.Kind == QuantityKind.LinearPosition && request.AbsoluteWidth.Value > 0 && InputBound(request.AbsoluteWidth.Value) &&
        request.MaximumWork >= 0 && request.MaximumWork <= 262144 && Precisions.Contains(request.MaximumPrecisionBits) &&
        request.MaximumRefinements >= 1 && request.MaximumRefinements <= 7;
    internal static CrankSliderInterval Point(Rational value) => new(value, value);
    internal static CrankSliderInterval Pi(CrankSliderNumerics.Context c) => c.FromGrid(
        c.GridSubtract(c.GridScale(CrankSliderNumerics.Atan(c, 5), 16), c.GridScale(CrankSliderNumerics.Atan(c, 239), 4)));
    internal static CrankSliderInterval Reciprocal(CrankSliderNumerics.Context c, CrankSliderInterval positive)
    {
        if (positive.Lower <= 0) throw new InvalidOperationException("Certified reciprocal requires a positive interval.");
        return c.Interval(c.Divide(1, positive.Upper), c.Divide(1, positive.Lower));
    }
    internal static CrankSliderInterval Multiply(CrankSliderNumerics.Context c, CrankSliderInterval a, CrankSliderInterval b)
    {
        var values = new[] { c.Multiply(a.Lower, b.Lower), c.Multiply(a.Lower, b.Upper), c.Multiply(a.Upper, b.Lower), c.Multiply(a.Upper, b.Upper) };
        return c.Interval(values.Min(), values.Max());
    }
    internal static CrankSliderInterval PiLength(CrankSliderNumerics.Context c, ExactPiLength value, CrankSliderInterval inversePi) =>
        c.Add(Point(value.RationalPartMm), c.Scale(inversePi, value.InversePiCoefficientMm));
    internal static CamNumericStatus Status(CrankSliderNumerics.Stop stop) => stop.Status == CrankSliderNumericStatus.NumericResourceLimit
        ? CamNumericStatus.NumericResourceLimit : CamNumericStatus.IncompleteNumericBudget;
}

public static class CamFollowerNumerics
{
    /// <summary>Exact coefficient equality precedes certified ordering; unresolved work never supplies a guessed sign.</summary>
    public static CamPiComparison Compare(ExactPiLength left, ExactPiLength right, CamNumericRequest? request = null) =>
        CompareDetailed(left, right, request).Comparison;

    public static CamPiComparisonResult CompareDetailed(ExactPiLength left, ExactPiLength right, CamNumericRequest? request = null)
    {
        request ??= CamNumericRequest.Default;
        if (!CamNumericalPrimitives.Valid(request))
            return new(left, right, request, CamPiComparison.Unresolved, CamNumericStatus.InvalidNumericRequest, 0, 0, 0, null,
                "Invalid width, units, policy or bounded comparison options.");
        if (left == right) return new(left, right, request, CamPiComparison.Equal, CamNumericStatus.ExactValue, 0, 0, 0,
            new CamInterval(0, 0), "Exact rational and inverse-pi coefficients are identical.");
        var c = new CrankSliderNumerics.Context(request.MaximumWork);
        var precision = 0; var passes = 0; CamInterval? last = null;
        CamPiComparisonResult Result(CamPiComparison comparison, CamNumericStatus status, string detail) =>
            new(left, right, request, comparison, status, c.Work, precision, passes, last, detail);
        try
        {
            foreach (var value in new[] { left.RationalPartMm, left.InversePiCoefficientMm, right.RationalPartMm, right.InversePiCoefficientMm }) c.Check(value);
            var r = c.Subtract(left.RationalPartMm, right.RationalPartMm);
            var coefficient = c.Subtract(left.InversePiCoefficientMm, right.InversePiCoefficientMm);
            if (coefficient.IsZero) return Result(r.Sign < 0 ? CamPiComparison.Less : CamPiComparison.Greater,
                CamNumericStatus.ExactValue, "Inverse-pi terms cancel; exact rational ordering decides the sign.");
            if (r.Sign >= 0 && coefficient.Sign > 0) return Result(CamPiComparison.Greater, CamNumericStatus.ExactValue, "Both terms are nonnegative and one is positive.");
            if (r.Sign <= 0 && coefficient.Sign < 0) return Result(CamPiComparison.Less, CamNumericStatus.ExactValue, "Both terms are nonpositive and one is negative.");
            foreach (var bits in CamNumericalPrimitives.Precisions)
            {
                if (bits > request.MaximumPrecisionBits) break;
                if (passes == request.MaximumRefinements)
                    return Result(CamPiComparison.Unresolved, CamNumericStatus.IncompleteNumericBudget, "Comparison refinement-pass budget exhausted.");
                c.Step(); c.SetPrecision(bits); precision = bits; passes++;
                var pi = CamNumericalPrimitives.Pi(c);
                // pi>0, so r+c/pi has the sign of r*pi+c. No division or display epsilon is needed.
                var sign = c.Add(c.Scale(pi, r), CamNumericalPrimitives.Point(coefficient));
                last = new CamInterval(sign);
                if (sign.Upper < 0) return Result(CamPiComparison.Less, CamNumericStatus.EnclosedValue, "Certified pi-scaled difference is strictly negative.");
                if (sign.Lower > 0) return Result(CamPiComparison.Greater, CamNumericStatus.EnclosedValue, "Certified pi-scaled difference is strictly positive.");
            }
            return Result(CamPiComparison.Unresolved, CamNumericStatus.NumericResourceLimit, "Maximum pi precision did not separate the exact comparison from zero.");
        }
        catch (CrankSliderNumerics.Stop stop)
        { return Result(CamPiComparison.Unresolved, CamNumericalPrimitives.Status(stop), stop.Message); }
    }

    public static CamNumericResult Evaluate(CamContourPointRecipe recipe, CamNumericRequest? request = null)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        return EvaluatePoint(recipe.RecipeId, request, c =>
        {
            foreach (var value in new[] { recipe.Support.HeightMm, recipe.Support.FirstDerivativeMmPerTurn,
                recipe.CenterMm.X, recipe.CenterMm.Y, recipe.CenterMm.Z }) c.Check(value);
        }, c =>
        {
            var phase = recipe.NormalTurns;
            var quarter = c.Multiply(phase, 4); CrankSliderInterval sin, cos;
            if (quarter.Denominator.IsOne)
            {
                var q = (int)quarter.Numerator;
                sin = CamNumericalPrimitives.Point(q == 1 ? 1 : q == 3 ? -1 : 0);
                cos = CamNumericalPrimitives.Point(q == 0 ? 1 : q == 2 ? -1 : 0);
            }
            else (sin, cos) = CrankSliderNumerics.SinCos(c, phase);
            var k = recipe.Support.FirstDerivativeMmPerTurn.IsZero ? CamNumericalPrimitives.Point(0) :
                c.Scale(CamNumericalPrimitives.Reciprocal(c, CamNumericalPrimitives.Pi(c)), c.Divide(recipe.Support.FirstDerivativeMmPerTurn, 2));
            var along = c.Subtract(c.Scale(cos, recipe.Support.HeightMm), CamNumericalPrimitives.Multiply(c, k, sin));
            var side = c.Add(c.Scale(sin, recipe.Support.HeightMm), CamNumericalPrimitives.Multiply(c, k, cos));
            return Vector(c, recipe.CenterMm, recipe.GuideDirection, along, recipe.TangentDirection, side);
        });
    }

    public static CamNumericResult Evaluate(ExactPiVector3 point, CamNumericRequest? request = null)
    {
        var inputId = HashText(Pack("cam-exact-pi-point-v1", V(point.RationalPartMm), V(point.InversePiCoefficientMm)));
        return EvaluatePoint(inputId, request, c =>
        {
            foreach (var value in new[] { point.RationalPartMm.X, point.RationalPartMm.Y, point.RationalPartMm.Z,
                point.InversePiCoefficientMm.X, point.InversePiCoefficientMm.Y, point.InversePiCoefficientMm.Z }) c.Check(value);
        }, c =>
        {
            var inverse = point.InversePiCoefficientMm == ExactVector3.Zero ? CamNumericalPrimitives.Point(0) :
                CamNumericalPrimitives.Reciprocal(c, CamNumericalPrimitives.Pi(c));
            return new CamVectorInterval(CamNumericalPrimitives.PiLength(c, point.X, inverse),
                CamNumericalPrimitives.PiLength(c, point.Y, inverse), CamNumericalPrimitives.PiLength(c, point.Z, inverse));
        });
    }

    private static CamNumericResult EvaluatePoint(string id, CamNumericRequest? request,
        Action<CrankSliderNumerics.Context> check, Func<CrankSliderNumerics.Context, CamVectorInterval> compute)
    {
        request ??= CamNumericRequest.Default;
        if (!CamNumericalPrimitives.Valid(request))
            return new(id, request, CamNumericStatus.InvalidNumericRequest, 0, 0, 0, null, null, "Invalid width, units, policy or bounded numerical options.");
        var c = new CrankSliderNumerics.Context(request.MaximumWork);
        CamVectorInterval? last = null; var precision = 0; var passes = 0;
        try
        {
            check(c);
            foreach (var bits in CamNumericalPrimitives.Precisions)
            {
                if (bits > request.MaximumPrecisionBits) break;
                if (passes == request.MaximumRefinements)
                    return new(id, request, CamNumericStatus.IncompleteNumericBudget, c.Work, precision, passes, null, last, "Refinement-pass budget exhausted.");
                c.Step(); c.SetPrecision(bits); precision = bits; passes++;
                last = compute(c);
                var coordinates = new[] { last.X, last.Y, last.Z };
                if (coordinates.All(value => c.Subtract(value.Upper, value.Lower) <= request.AbsoluteWidth.Value))
                    return new(id, request, coordinates.All(value => value.IsExact) ? CamNumericStatus.ExactValue : CamNumericStatus.EnclosedValue,
                        c.Work, precision, passes, last, null, "Requested world point enclosure widths satisfied; device admission is separate.");
            }
            return new(id, request, CamNumericStatus.NumericResourceLimit, c.Work, precision, passes, null, last, "Maximum precision reached before requested point widths were proved.");
        }
        catch (CrankSliderNumerics.Stop stop)
        { return new(id, request, CamNumericalPrimitives.Status(stop), c.Work, precision, passes, null, last, stop.Message); }
    }

    private static CamVectorInterval Vector(CrankSliderNumerics.Context c, ExactVector3 origin, ExactVector3 g,
        CrankSliderInterval along, ExactVector3 f, CrankSliderInterval side) => new(
        c.Add(CamNumericalPrimitives.Point(origin.X), c.Add(c.Scale(along, g.X), c.Scale(side, f.X))),
        c.Add(CamNumericalPrimitives.Point(origin.Y), c.Add(c.Scale(along, g.Y), c.Scale(side, f.Y))),
        c.Add(CamNumericalPrimitives.Point(origin.Z), c.Add(c.Scale(along, g.Z), c.Scale(side, f.Z))));
}

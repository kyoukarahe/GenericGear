using System;

namespace GearInvest.Core;

/// <summary>Exact millimeters r+c/pi, with rational coefficients and the fixed mathematical constant pi.
/// This is not a new unit, expression evaluator, ordered real field or rounded Rational.</summary>
public readonly struct ExactPiLength : IEquatable<ExactPiLength>
{
    public ExactPiLength(Rational rationalPartMm, Rational inversePiCoefficientMm)
        : this(rationalPartMm, inversePiCoefficientMm, 128) { }
    private ExactPiLength(Rational rationalPartMm, Rational inversePiCoefficientMm, int digits)
    {
        ExactQuantity.Bound(rationalPartMm, digits); ExactQuantity.Bound(inversePiCoefficientMm, digits);
        RationalPartMm = rationalPartMm; InversePiCoefficientMm = inversePiCoefficientMm;
    }
    public Rational RationalPartMm { get; }
    public Rational InversePiCoefficientMm { get; }
    public string Unit => "mm";
    public static ExactPiLength FromCanonical(Rational rationalPartMm, Rational inversePiCoefficientMm) => new(rationalPartMm, inversePiCoefficientMm, 8192);
    public static ExactPiLength FromMillimeters(ExactQuantity value)
    {
        if (value.Kind != QuantityKind.LinearPosition) throw new ArgumentException("DimensionMismatch: exact pi length requires millimeters.");
        return FromCanonical(value.Value, 0);
    }
    public static ExactPiLength operator +(ExactPiLength a, ExactPiLength b) => FromCanonical(a.RationalPartMm + b.RationalPartMm, a.InversePiCoefficientMm + b.InversePiCoefficientMm);
    public static ExactPiLength operator -(ExactPiLength a, ExactPiLength b) => FromCanonical(a.RationalPartMm - b.RationalPartMm, a.InversePiCoefficientMm - b.InversePiCoefficientMm);
    public static ExactPiLength operator -(ExactPiLength a) => FromCanonical(-a.RationalPartMm, -a.InversePiCoefficientMm);
    public static ExactPiLength operator *(ExactPiLength a, Rational scale) => FromCanonical(a.RationalPartMm * scale, a.InversePiCoefficientMm * scale);
    public static ExactPiLength operator *(Rational scale, ExactPiLength a) => a * scale;
    public bool Equals(ExactPiLength other) => RationalPartMm == other.RationalPartMm && InversePiCoefficientMm == other.InversePiCoefficientMm;
    public override bool Equals(object? obj) => obj is ExactPiLength other && Equals(other);
    public override int GetHashCode() { unchecked { return RationalPartMm.GetHashCode() * 397 ^ InversePiCoefficientMm.GetHashCode(); } }
    public static bool operator ==(ExactPiLength a, ExactPiLength b) => a.Equals(b);
    public static bool operator !=(ExactPiLength a, ExactPiLength b) => !a.Equals(b);
    public override string ToString() => RationalPartMm + "+(" + InversePiCoefficientMm + ")/pi mm";
    /// <summary>Display only. Refuses nonfinite components/results; never supplies a mechanical input or proof.</summary>
    public double ApproximateMillimeters()
    {
        var result = Approximate(RationalPartMm) + Approximate(InversePiCoefficientMm) / Math.PI;
        if (double.IsNaN(result) || double.IsInfinity(result)) throw new ArgumentException("Display approximation resource limit exceeded.");
        return result;
    }
    private static double Approximate(Rational value)
    {
        var n = (double)value.Numerator; var d = (double)value.Denominator;
        if (double.IsNaN(n) || double.IsInfinity(n) || double.IsNaN(d) || double.IsInfinity(d) || d == 0)
            throw new ArgumentException("Display approximation resource limit exceeded.");
        return n / d;
    }
}

/// <summary>Exact mm point/free displacement P=r+c/pi. Existing caller-tick ExactVector3 is unchanged.</summary>
public readonly struct ExactPiVector3 : IEquatable<ExactPiVector3>
{
    public ExactPiVector3(ExactVector3 rationalPartMm, ExactVector3 inversePiCoefficientMm)
        : this(rationalPartMm, inversePiCoefficientMm, 128) { }
    private ExactPiVector3(ExactVector3 rationalPartMm, ExactVector3 inversePiCoefficientMm, int digits)
    {
        foreach (var n in new[] { rationalPartMm.X, rationalPartMm.Y, rationalPartMm.Z, inversePiCoefficientMm.X, inversePiCoefficientMm.Y, inversePiCoefficientMm.Z }) ExactQuantity.Bound(n, digits);
        RationalPartMm = rationalPartMm; InversePiCoefficientMm = inversePiCoefficientMm;
    }
    public ExactVector3 RationalPartMm { get; }
    public ExactVector3 InversePiCoefficientMm { get; }
    public string Unit => "mm";
    public ExactPiLength X => ExactPiLength.FromCanonical(RationalPartMm.X, InversePiCoefficientMm.X);
    public ExactPiLength Y => ExactPiLength.FromCanonical(RationalPartMm.Y, InversePiCoefficientMm.Y);
    public ExactPiLength Z => ExactPiLength.FromCanonical(RationalPartMm.Z, InversePiCoefficientMm.Z);
    public static ExactPiVector3 Zero => default;
    public static ExactPiVector3 FromCanonical(ExactVector3 rationalPartMm, ExactVector3 inversePiCoefficientMm) => new(rationalPartMm, inversePiCoefficientMm, 8192);
    public static ExactPiVector3 FromMillimeters(ExactVector3 pointMm) => FromCanonical(pointMm, ExactVector3.Zero);
    public static ExactPiVector3 operator +(ExactPiVector3 a, ExactPiVector3 b) => FromCanonical(a.RationalPartMm + b.RationalPartMm, a.InversePiCoefficientMm + b.InversePiCoefficientMm);
    public static ExactPiVector3 operator -(ExactPiVector3 a, ExactPiVector3 b) => FromCanonical(a.RationalPartMm - b.RationalPartMm, a.InversePiCoefficientMm - b.InversePiCoefficientMm);
    public static ExactPiVector3 operator -(ExactPiVector3 a) => FromCanonical(-a.RationalPartMm, -a.InversePiCoefficientMm);
    public static ExactPiVector3 operator *(ExactPiVector3 a, Rational scale) => FromCanonical(a.RationalPartMm * scale, a.InversePiCoefficientMm * scale);
    public static ExactPiVector3 operator *(Rational scale, ExactPiVector3 a) => a * scale;
    public ExactPiLength Dot(ExactVector3 dimensionless) => ExactPiLength.FromCanonical(RationalPartMm.Dot(dimensionless), InversePiCoefficientMm.Dot(dimensionless));
    public ExactPiVector3 Transform(OrientedFrame poseMm)
    {
        if (poseMm is null || !poseMm.IsProperCardinal) throw new ArgumentException("Proper cardinal pose required for exact pi geometry.");
        return FromCanonical(poseMm.Point(RationalPartMm), poseMm.Vector(InversePiCoefficientMm));
    }
    public bool Equals(ExactPiVector3 other) => RationalPartMm == other.RationalPartMm && InversePiCoefficientMm == other.InversePiCoefficientMm;
    public override bool Equals(object? obj) => obj is ExactPiVector3 other && Equals(other);
    public override int GetHashCode() { unchecked { return RationalPartMm.GetHashCode() * 397 ^ InversePiCoefficientMm.GetHashCode(); } }
    public static bool operator ==(ExactPiVector3 a, ExactPiVector3 b) => a.Equals(b);
    public static bool operator !=(ExactPiVector3 a, ExactPiVector3 b) => !a.Equals(b);
    public (double X, double Y, double Z) ApproximateMillimeters() => (X.ApproximateMillimeters(), Y.ApproximateMillimeters(), Z.ApproximateMillimeters());
    public override string ToString() => RationalPartMm + "+" + InversePiCoefficientMm + "/pi mm";
}

/// <summary>Explicit mm origin r+c/pi and dimensionless X/Y/Z columns. Z is increasing guide position.</summary>
public sealed class ExactPiFrame
{
    public ExactPiFrame(ExactPiVector3 origin, ExactVector3 x, ExactVector3 y, ExactVector3 z)
    { Origin = origin; X = x; Y = y; Z = z; }
    public ExactPiVector3 Origin { get; }
    public ExactVector3 X { get; }
    public ExactVector3 Y { get; }
    public ExactVector3 Z { get; }
    public bool IsProperCardinal => X.IsCardinal && Y.IsCardinal && Z.IsCardinal && X.Dot(Y) == 0 && X.Cross(Y) == Z;
    public ExactPiFrame At(ExactPiVector3 origin) => new(origin, X, Y, Z);
    public ExactPiFrame Transform(OrientedFrame poseMm) => new(Origin.Transform(poseMm), poseMm.Vector(X), poseMm.Vector(Y), poseMm.Vector(Z));
}

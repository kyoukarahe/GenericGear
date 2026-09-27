using System;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum GenevaLengthKind { RationalMm, SinTurns }

/// <summary>A bounded authored length construction. Its angle never changes when the assembly slot count changes.</summary>
public sealed class GenevaLength : IEquatable<GenevaLength>
{
    private GenevaLength(GenevaLengthKind kind, Rational coefficient, Rational angle)
    { MechanicalDerivedNumbers.Check(coefficient); MechanicalDerivedNumbers.Check(angle); Kind = kind; CoefficientMm = coefficient; AngleTurns = angle; }
    public GenevaLengthKind Kind { get; }
    public Rational CoefficientMm { get; }
    /// <summary>Normalized to [0,1/4]; sign is carried by the coefficient. Zero for rational lengths.</summary>
    public Rational AngleTurns { get; }
    public Rational? ExactMillimeters => Kind == GenevaLengthKind.RationalMm ? CoefficientMm : null;
    public string CanonicalRepresentation => Pack(Kind.ToString(), F(CoefficientMm), F(AngleTurns));
    public static GenevaLength Millimeters(Rational value)
    { CheckInput(value); return new(GenevaLengthKind.RationalMm, value, 0); }
    public static GenevaLength SinTurns(Rational coefficientMm, Rational angleTurns)
    { CheckInput(coefficientMm); CheckInput(angleTurns); return Normalize(coefficientMm, angleTurns); }
    public static GenevaLength CosTurns(Rational coefficientMm, Rational angleTurns)
    { CheckInput(coefficientMm); CheckInput(angleTurns); return Normalize(coefficientMm, angleTurns + new Rational(1, 4)); }
    /// <summary>Reads an already normalized construction under the existing derived 8192-character bound.</summary>
    public static GenevaLength FromCanonical(GenevaLengthKind kind, Rational coefficientMm, Rational angleTurns)
    {
        MechanicalDerivedNumbers.Check(coefficientMm); MechanicalDerivedNumbers.Check(angleTurns);
        if (kind == GenevaLengthKind.RationalMm && angleTurns.IsZero) return new(kind, coefficientMm, 0);
        if (kind == GenevaLengthKind.SinTurns && !coefficientMm.IsZero && angleTurns > 0 && angleTurns < new Rational(1, 4) &&
            angleTurns != new Rational(1, 12)) return new(kind, coefficientMm, angleTurns);
        throw new ArgumentException("Geneva canonical length must already have normalized kind, coefficient and angle.");
    }
    private static GenevaLength Normalize(Rational coefficient, Rational angle)
    {
        var rem = angle.Numerator % angle.Denominator;
        if (rem.Sign < 0) rem += angle.Denominator;
        angle = new Rational(rem, angle.Denominator);
        if (angle > new Rational(1, 2)) { angle -= new Rational(1, 2); coefficient = -coefficient; }
        if (angle > new Rational(1, 4)) angle = new Rational(1, 2) - angle;
        if (coefficient.IsZero || angle.IsZero) return new(GenevaLengthKind.RationalMm, 0, 0);
        if (angle == new Rational(1, 4)) return new(GenevaLengthKind.RationalMm, coefficient, 0);
        if (angle == new Rational(1, 12)) return new(GenevaLengthKind.RationalMm, coefficient / 2, 0);
        return new(GenevaLengthKind.SinTurns, coefficient, angle);
    }
    internal static void CheckInput(Rational value)
    { if (!CamNumericalPrimitives.InputBound(value)) throw new ArgumentException("Geneva authored rational exceeds the 128-character component bound."); }
    public bool Equals(GenevaLength? other) => other is not null && Kind == other.Kind && CoefficientMm == other.CoefficientMm && AngleTurns == other.AngleTurns;
    public override bool Equals(object? obj) => obj is GenevaLength length && Equals(length);
    public override int GetHashCode() => CanonicalRepresentation.GetHashCode();
}

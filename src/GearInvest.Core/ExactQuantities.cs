using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace GearInvest.Core;

public enum QuantityKind { AngularPosition, LinearPosition, AngularPerAngular, LinearPerAngular, AngularPerLinear }

/// <summary>A bounded exact quantity. Canonical length is mm, angular position is unwrapped turns.</summary>
public readonly struct ExactQuantity : IEquatable<ExactQuantity>
{
    public ExactQuantity(QuantityKind kind, Rational value, string unit)
    {
        Bound(value, 128); Kind = kind;
        var canonical = CanonicalUnit(kind);
        Rational factor = unit == canonical ? Rational.One
            : kind == QuantityKind.LinearPosition && unit == "m" ? 1000
            : kind == QuantityKind.LinearPerAngular && unit == "m/turn" ? 1000
            : kind == QuantityKind.AngularPerLinear && unit == "turn/m" ? new Rational(1, 1000)
            : throw new ArgumentException("DimensionMismatch: unsupported unit for quantity kind.");
        Value = value * factor; Bound(Value, 8192);
    }
    private ExactQuantity(QuantityKind kind, Rational value)
    { _ = CanonicalUnit(kind); Bound(value, 8192); Kind = kind; Value = value; }
    public QuantityKind Kind { get; }
    public Rational Value { get; }
    public string Unit => CanonicalUnit(Kind);
    /// <summary>Canonical calculated value, with the derived 8192-digit bound. Input declarations enforce their own tighter bound.</summary>
    public static ExactQuantity FromCanonical(QuantityKind kind, Rational value) => new(kind, value);
    public static ExactQuantity Turns(Rational value) => new(QuantityKind.AngularPosition, value, "turn");
    public static ExactQuantity Millimeters(Rational value) => new(QuantityKind.LinearPosition, value, "mm");
    public static ExactQuantity MillimetersPerTurn(Rational value) => new(QuantityKind.LinearPerAngular, value, "mm/turn");
    public static ExactQuantity TurnsPerTurn(Rational value) => new(QuantityKind.AngularPerAngular, value, "turn/turn");
    public static ExactQuantity TurnsPerMillimeter(Rational value) => new(QuantityKind.AngularPerLinear, value, "turn/mm");
    public bool Equals(ExactQuantity other) => Kind == other.Kind && Value == other.Value;
    public override bool Equals(object? obj) => obj is ExactQuantity other && Equals(other);
    public override int GetHashCode() { unchecked { return (int)Kind * 397 ^ Value.GetHashCode(); } }
    public static bool operator ==(ExactQuantity a, ExactQuantity b) => a.Equals(b);
    public static bool operator !=(ExactQuantity a, ExactQuantity b) => !a.Equals(b);
    public static ExactQuantity operator +(ExactQuantity a, ExactQuantity b) => SameKind(a, b, a.Value + b.Value);
    public static ExactQuantity operator -(ExactQuantity a, ExactQuantity b) => SameKind(a, b, a.Value - b.Value);
    private static ExactQuantity SameKind(ExactQuantity a, ExactQuantity b, Rational value)
    { if (a.Kind != b.Kind) throw new ArgumentException("DimensionMismatch: quantities cannot be added or subtracted."); return FromCanonical(a.Kind, value); }
    public override string ToString() => Value + " " + Unit;
    private static string CanonicalUnit(QuantityKind kind) => kind switch
    {
        QuantityKind.AngularPosition => "turn", QuantityKind.LinearPosition => "mm",
        QuantityKind.AngularPerAngular => "turn/turn", QuantityKind.LinearPerAngular => "mm/turn",
        QuantityKind.AngularPerLinear => "turn/mm", _ => throw new ArgumentException("Unknown quantity kind.")
    };
    internal static void Bound(Rational value, int digits)
    {
        if (value.Numerator.ToString(CultureInfo.InvariantCulture).Length > digits || value.Denominator.ToString(CultureInfo.InvariantCulture).Length > digits)
            throw new ArgumentException("Exact quantity resource digit bound exceeded.");
    }
}

/// <summary>Finite closed interval of angular or linear positions. Null intersections mean empty, never unbounded.</summary>
public sealed class ExactQuantityInterval : IEquatable<ExactQuantityInterval>
{
    public ExactQuantityInterval(ExactQuantity lower, ExactQuantity upper)
    {
        if (lower.Kind != upper.Kind || (lower.Kind != QuantityKind.AngularPosition && lower.Kind != QuantityKind.LinearPosition))
            throw new ArgumentException("DimensionMismatch: interval requires positions of one quantity kind.");
        if (lower.Value > upper.Value) throw new ArgumentException("Closed interval lower endpoint exceeds upper endpoint.");
        Lower = lower; Upper = upper;
    }
    public ExactQuantity Lower { get; }
    public ExactQuantity Upper { get; }
    public QuantityKind Kind => Lower.Kind;
    public static ExactQuantityInterval Turns(Rational lower, Rational upper) => new(ExactQuantity.Turns(lower), ExactQuantity.Turns(upper));
    public static ExactQuantityInterval Millimeters(Rational lower, Rational upper) => new(ExactQuantity.Millimeters(lower), ExactQuantity.Millimeters(upper));
    public bool Contains(ExactQuantity value)
    { if (value.Kind != Kind) throw new ArgumentException("DimensionMismatch: interval membership."); return Lower.Value <= value.Value && value.Value <= Upper.Value; }
    public bool Contains(ExactQuantityInterval other)
    { if (other is null) throw new ArgumentNullException(nameof(other)); return Contains(other.Lower) && Contains(other.Upper); }
    public ExactQuantityInterval? Intersect(ExactQuantityInterval other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (other.Kind != Kind) throw new ArgumentException("DimensionMismatch: interval intersection.");
        var lower = Lower.Value >= other.Lower.Value ? Lower : other.Lower;
        var upper = Upper.Value <= other.Upper.Value ? Upper : other.Upper;
        return lower.Value <= upper.Value ? new ExactQuantityInterval(lower, upper) : null;
    }
    /// <summary>Inverse image under y=gain*x+offset. Endpoint order is reversed exactly for negative gain.</summary>
    public ExactQuantityInterval InverseAffine(Rational gain, Rational offset, QuantityKind inputKind)
    {
        ExactQuantity.Bound(gain, 8192); ExactQuantity.Bound(offset, 8192);
        if (gain.IsZero) throw new ArgumentException("Nonzero affine gain required for a finite interval inverse.");
        var a = (Lower.Value - offset) / gain; var b = (Upper.Value - offset) / gain;
        return new ExactQuantityInterval(ExactQuantity.FromCanonical(inputKind, a <= b ? a : b), ExactQuantity.FromCanonical(inputKind, a <= b ? b : a));
    }
    public bool Equals(ExactQuantityInterval? other) => other is not null && Lower == other.Lower && Upper == other.Upper;
    public override bool Equals(object? obj) => obj is ExactQuantityInterval other && Equals(other);
    public override int GetHashCode() { unchecked { return Lower.GetHashCode() * 397 ^ Upper.GetHashCode(); } }
    public override string ToString() => "[" + Lower.Value + "," + Upper.Value + "] " + Lower.Unit;
}

/// <summary>Dimensioned affine law from one unwrapped angular input. Never accepts length as an angle.</summary>
public readonly struct DimensionedAffineRelation : IEquatable<DimensionedAffineRelation>
{
    public DimensionedAffineRelation(ExactQuantity gain, ExactQuantity offset)
    {
        if (!(gain.Kind == QuantityKind.LinearPerAngular && offset.Kind == QuantityKind.LinearPosition) &&
            !(gain.Kind == QuantityKind.AngularPerAngular && offset.Kind == QuantityKind.AngularPosition))
            throw new ArgumentException("DimensionMismatch: affine gain and offset.");
        Gain = gain; Offset = offset;
    }
    public ExactQuantity Gain { get; }
    public ExactQuantity Offset { get; }
    public ExactQuantity Evaluate(ExactQuantity input)
    {
        if (!(Gain.Kind == QuantityKind.LinearPerAngular && Offset.Kind == QuantityKind.LinearPosition) &&
            !(Gain.Kind == QuantityKind.AngularPerAngular && Offset.Kind == QuantityKind.AngularPosition))
            throw new ArgumentException("DimensionMismatch: uninitialized or invalid affine relation.");
        if (input.Kind != QuantityKind.AngularPosition) throw new ArgumentException("DimensionMismatch: affine input must be unwrapped turns.");
        return ExactQuantity.FromCanonical(Offset.Kind, Gain.Value * input.Value + Offset.Value);
    }
    public bool Equals(DimensionedAffineRelation other) => Gain == other.Gain && Offset == other.Offset;
    public override bool Equals(object? obj) => obj is DimensionedAffineRelation other && Equals(other);
    public override int GetHashCode() { unchecked { return Gain.GetHashCode() * 397 ^ Offset.GetHashCode(); } }
}

public sealed class QuantityDof
{
    public QuantityDof(string id, QuantityKind kind, bool isPrescribed = false)
    {
        Id = OrientedIds.Require(id);
        if (kind != QuantityKind.AngularPosition && kind != QuantityKind.LinearPosition) throw new ArgumentException("DOF quantity must be a position.");
        Kind = kind; IsPrescribed = isPrescribed;
    }
    public string Id { get; }
    public QuantityKind Kind { get; }
    public bool IsPrescribed { get; }
}

public sealed class QuantityAffineCoupling
{
    public QuantityAffineCoupling(string id, string driverDofId, string drivenDofId, ExactQuantity transfer, ExactQuantity offset)
    { Id = OrientedIds.Require(id); DriverDofId = OrientedIds.Require(driverDofId); DrivenDofId = OrientedIds.Require(drivenDofId); Transfer = transfer; Offset = offset; }
    public string Id { get; }
    public string DriverDofId { get; }
    public string DrivenDofId { get; }
    public ExactQuantity Transfer { get; }
    public ExactQuantity Offset { get; }
}

/// <summary>Unit-validated adapter over the same scalar traversal used by the legacy rotational analyzer.</summary>
public static class QuantityAffineComponentAnalyzer
{
    public static ReadOnlyCollection<AffineComponentAnalysis> Analyze(string? selectedInputId,
        IEnumerable<QuantityDof> dofSource, IEnumerable<QuantityAffineCoupling> constraintSource)
    {
        if (dofSource is null || constraintSource is null) throw new ArgumentNullException();
        var nodes = dofSource.Take(66).ToArray(); var edges = constraintSource.Take(194).ToArray();
        if (nodes.Length > 65 || edges.Length > 193 || nodes.Any(n => n is null) || edges.Any(e => e is null) ||
            nodes.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Length)
            throw new ArgumentException("Bounded unique quantity graph required.");
        var map = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (!map.TryGetValue(e.DriverDofId, out var a) || !map.TryGetValue(e.DrivenDofId, out var b)) throw new ArgumentException("Unknown quantity graph endpoint.");
            var valid = a.Kind == QuantityKind.AngularPosition && b.Kind == QuantityKind.AngularPosition && e.Transfer.Kind == QuantityKind.AngularPerAngular
                || a.Kind == QuantityKind.AngularPosition && b.Kind == QuantityKind.LinearPosition && e.Transfer.Kind == QuantityKind.LinearPerAngular
                || a.Kind == QuantityKind.LinearPosition && b.Kind == QuantityKind.AngularPosition && e.Transfer.Kind == QuantityKind.AngularPerLinear;
            if (!valid || e.Offset.Kind != b.Kind) throw new ArgumentException("DimensionMismatch: coupling endpoints, gain and offset.");
        }
        return AffineComponentAnalyzer.AnalyzeScalars(selectedInputId, nodes.Select(n => new ScalarCoordinate(n.Id, n.IsPrescribed)),
            edges.Select(e => new ScalarAffineCoupling(e.Id, e.DriverDofId, e.DrivenDofId, e.Transfer.Value, e.Offset.Value)), 65, 193, 8192);
    }
}

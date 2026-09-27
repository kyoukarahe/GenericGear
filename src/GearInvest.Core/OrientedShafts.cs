using System;

namespace GearInvest.Core;

/// <summary>Exact world point or free vector. Units are caller-declared ticks, not manufacturing millimetres.</summary>
public readonly struct ExactVector3 : IEquatable<ExactVector3>
{
    public ExactVector3(Rational x, Rational y, Rational z) { X = x; Y = y; Z = z; }
    public Rational X { get; }
    public Rational Y { get; }
    public Rational Z { get; }
    public static ExactVector3 Zero => default;
    public static ExactVector3 UnitX => new(1, 0, 0);
    public static ExactVector3 UnitY => new(0, 1, 0);
    public static ExactVector3 UnitZ => new(0, 0, 1);
    public Rational LengthSquared => Dot(this);
    public bool IsCardinal => LengthSquared == 1 && new[] { X, Y, Z }.AllCardinal();
    public Rational Dot(ExactVector3 other) => X * other.X + Y * other.Y + Z * other.Z;
    public ExactVector3 Cross(ExactVector3 other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
    public static ExactVector3 operator +(ExactVector3 a, ExactVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static ExactVector3 operator -(ExactVector3 a, ExactVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static ExactVector3 operator -(ExactVector3 a) => new(-a.X, -a.Y, -a.Z);
    public static ExactVector3 operator *(ExactVector3 a, Rational b) => new(a.X * b, a.Y * b, a.Z * b);
    public static ExactVector3 operator *(Rational a, ExactVector3 b) => b * a;
    public bool Equals(ExactVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is ExactVector3 other && Equals(other);
    public override int GetHashCode() { unchecked { return (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode(); } }
    public static bool operator ==(ExactVector3 a, ExactVector3 b) => a.Equals(b);
    public static bool operator !=(ExactVector3 a, ExactVector3 b) => !a.Equals(b);
    public override string ToString() => $"({X},{Y},{Z})";
}

internal static class CardinalComponents
{
    internal static bool AllCardinal(this Rational[] values)
    {
        foreach (var value in values) if (value != 0 && value != 1 && value != -1) return false;
        return true;
    }
}

/// <summary>Columns X/Y/Z form a right-handed cardinal basis; Z defines positive right-hand rotation, X zero angle.</summary>
public sealed class OrientedFrame
{
    public OrientedFrame(ExactVector3 origin, ExactVector3 x, ExactVector3 y, ExactVector3 z) { Origin = origin; X = x; Y = y; Z = z; }
    public ExactVector3 Origin { get; }
    public ExactVector3 X { get; }
    public ExactVector3 Y { get; }
    public ExactVector3 Z { get; }
    public static OrientedFrame Identity => new(default, ExactVector3.UnitX, ExactVector3.UnitY, ExactVector3.UnitZ);
    public bool IsProperCardinal => X.IsCardinal && Y.IsCardinal && Z.IsCardinal && X.Dot(Y) == 0 && X.Cross(Y) == Z;
    public ExactVector3 Vector(ExactVector3 local) => X * local.X + Y * local.Y + Z * local.Z;
    public ExactVector3 Point(ExactVector3 local) => Origin + Vector(local);
    public OrientedFrame Transform(OrientedFrame local) => new(Point(local.Origin), Vector(local.X), Vector(local.Y), Vector(local.Z));
    public OrientedFrame At(ExactVector3 station) => new(station, X, Y, Z);
}

public sealed class OrientedShaft
{
    public OrientedShaft(string id, OrientedFrame frame, bool isPrescribed = false)
    { Id = OrientedIds.Require(id); Frame = frame ?? throw new ArgumentNullException(nameof(frame)); IsPrescribed = isPrescribed; }
    public string Id { get; }
    public OrientedFrame Frame { get; }
    public bool IsPrescribed { get; }
    public bool Contains(ExactVector3 point) => (point - Frame.Origin).Cross(Frame.Z) == ExactVector3.Zero;
    public bool SameLine(OrientedShaft other) => Frame.Z.Cross(other.Frame.Z) == ExactVector3.Zero && Contains(other.Frame.Origin);
}

public enum ShaftConnectionKind { RigidZeroPhase }

public sealed class ShaftPort
{
    public ShaftPort(string id, string shaftId, OrientedFrame frame, Rational phaseOffset = default,
        ShaftConnectionKind kind = ShaftConnectionKind.RigidZeroPhase)
    { Id = OrientedIds.Require(id); ShaftId = OrientedIds.Require(shaftId); Frame = frame ?? throw new ArgumentNullException(nameof(frame)); PhaseOffset = phaseOffset; Kind = kind; }
    public string Id { get; }
    public string ShaftId { get; }
    public OrientedFrame Frame { get; }
    public Rational PhaseOffset { get; }
    public ShaftConnectionKind Kind { get; }
}

internal static class OrientedIds
{
    internal static string Require(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 160) throw new ArgumentException("A nonempty stable ID of at most 160 characters is required.");
        return id;
    }
}

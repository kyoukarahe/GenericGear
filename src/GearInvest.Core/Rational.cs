using System;
using System.Globalization;
using System.Numerics;

namespace GearInvest.Core;

public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>, IComparable
{
    private readonly BigInteger _numerator;
    private readonly BigInteger _denominator;

    public Rational(BigInteger numerator)
        : this(numerator, BigInteger.One)
    {
    }

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException("A rational denominator cannot be zero.");
        }

        if (numerator.IsZero)
        {
            _numerator = BigInteger.Zero;
            _denominator = BigInteger.One;
            return;
        }

        if (denominator.Sign < 0)
        {
            numerator = BigInteger.Negate(numerator);
            denominator = BigInteger.Negate(denominator);
        }

        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        _numerator = numerator / divisor;
        _denominator = denominator / divisor;
    }

    public BigInteger Numerator => _numerator;

    public BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;

    public int Sign => Numerator.Sign;

    public bool IsZero => Numerator.IsZero;

    public static Rational Zero => new(BigInteger.Zero, BigInteger.One);

    public static Rational One => new(BigInteger.One, BigInteger.One);

    public static Rational Parse(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var text = value.Trim();
        if (text.Length == 0)
        {
            throw new FormatException("A rational value cannot be empty.");
        }

        var separator = text.IndexOf('/');
        if (separator < 0)
        {
            return new Rational(ParseInteger(text));
        }

        if (separator == 0 || separator == text.Length - 1 || text.IndexOf('/', separator + 1) >= 0)
        {
            throw new FormatException($"'{value}' is not an integer or numerator/denominator fraction.");
        }

        var numerator = ParseInteger(text.Substring(0, separator));
        var denominator = ParseInteger(text.Substring(separator + 1));
        return new Rational(numerator, denominator);
    }

    public static bool TryParse(string? value, out Rational result)
    {
        try
        {
            if (value is null)
            {
                result = Zero;
                return false;
            }

            result = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            result = Zero;
            return false;
        }
        catch (DivideByZeroException)
        {
            result = Zero;
            return false;
        }
    }

    public int CompareTo(Rational other)
    {
        return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    int IComparable.CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (obj is not Rational rational)
        {
            throw new ArgumentException($"Object must be of type {nameof(Rational)}.", nameof(obj));
        }

        return CompareTo(rational);
    }

    public bool Equals(Rational other)
    {
        return Numerator.Equals(other.Numerator) && Denominator.Equals(other.Denominator);
    }

    public override bool Equals(object? obj)
    {
        return obj is Rational rational && Equals(rational);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Numerator.GetHashCode() * 397) ^ Denominator.GetHashCode();
        }
    }

    public override string ToString()
    {
        var numerator = Numerator.ToString(CultureInfo.InvariantCulture);
        return Denominator.IsOne
            ? numerator
            : numerator + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
    }

    public static Rational operator +(Rational left, Rational right)
    {
        return new Rational(
            (left.Numerator * right.Denominator) + (right.Numerator * left.Denominator),
            left.Denominator * right.Denominator);
    }

    public static Rational operator -(Rational left, Rational right)
    {
        return new Rational(
            (left.Numerator * right.Denominator) - (right.Numerator * left.Denominator),
            left.Denominator * right.Denominator);
    }

    public static Rational operator -(Rational value)
    {
        return new Rational(BigInteger.Negate(value.Numerator), value.Denominator);
    }

    public static Rational operator *(Rational left, Rational right)
    {
        return new Rational(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    }

    public static Rational operator /(Rational left, Rational right)
    {
        if (right.IsZero)
        {
            throw new DivideByZeroException("Cannot divide by a zero rational.");
        }

        return new Rational(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
    }

    public static bool operator ==(Rational left, Rational right) => left.Equals(right);

    public static bool operator !=(Rational left, Rational right) => !left.Equals(right);

    public static bool operator <(Rational left, Rational right) => left.CompareTo(right) < 0;

    public static bool operator <=(Rational left, Rational right) => left.CompareTo(right) <= 0;

    public static bool operator >(Rational left, Rational right) => left.CompareTo(right) > 0;

    public static bool operator >=(Rational left, Rational right) => left.CompareTo(right) >= 0;

    public static implicit operator Rational(int value) => new(value);

    public static implicit operator Rational(long value) => new(value);

    private static BigInteger ParseInteger(string value)
    {
        if (!BigInteger.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var result))
        {
            throw new FormatException($"'{value}' is not a canonical integer value.");
        }

        return result;
    }
}

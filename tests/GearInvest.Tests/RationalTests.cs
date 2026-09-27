using System;
using System.Globalization;
using System.Numerics;
using GearInvest.Core;
using Xunit;

namespace GearInvest.Tests;

public sealed class RationalTests
{
    [Theory]
    [InlineData("2/4", "1/2")]
    [InlineData("1/-2", "-1/2")]
    [InlineData("0/123", "0")]
    [InlineData("-12/-18", "2/3")]
    [InlineData("42", "42")]
    public void NormalizesToCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, Rational.Parse(input).ToString());
    }

    [Fact]
    public void DefaultValueIsCanonicalZero()
    {
        Rational value = default;
        Assert.Equal(BigInteger.Zero, value.Numerator);
        Assert.Equal(BigInteger.One, value.Denominator);
        Assert.Equal("0", value.ToString());
    }

    [Fact]
    public void PerformsExactArithmeticAndComparison()
    {
        var oneThird = Rational.Parse("1/3");
        var oneSixth = Rational.Parse("1/6");

        Assert.Equal(Rational.Parse("1/2"), oneThird + oneSixth);
        Assert.Equal(Rational.Parse("1/6"), oneThird - oneSixth);
        Assert.Equal(Rational.Parse("1/18"), oneThird * oneSixth);
        Assert.Equal(new Rational(2), oneThird / oneSixth);
        Assert.True(oneThird > oneSixth);
        Assert.Equal(-1, Rational.Parse("-3/5").Sign);
    }

    [Fact]
    public void RejectsZeroDenominatorAndDivisionByZero()
    {
        Assert.Throws<DivideByZeroException>(() => new Rational(1, 0));
        Assert.Throws<DivideByZeroException>(() => Rational.One / Rational.Zero);
        Assert.False(Rational.TryParse("1/0", out _));
    }

    [Fact]
    public void PreservesVeryLargeBigIntegerExactly()
    {
        var large = BigInteger.Pow(new BigInteger(10), 200) + 12345;
        var value = new Rational(large * 7, 21);

        Assert.Equal(new Rational(large, 3), value);
        Assert.Equal(large, value.Numerator);
        Assert.Equal(new BigInteger(3), value.Denominator);
    }

    [Fact]
    public void ParsingAndFormattingAreCultureIndependent()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("-123456789/1000", Rational.Parse("-123456789/1000").ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}

namespace Norvia.Docs.Diagrams.UnitTests;

public sealed class NumTests
{
    [Theory]
    [InlineData(0.57, "0.57")]
    [InlineData(1.5707963267948966, "1.5707963267948966")]
    [InlineData(1e-05, "1e-05")]
    [InlineData(1e16, "1e+16")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(123456789.123, "123456789.123")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00012345, "0.00012345")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(100.0, "100.0")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(9007199254740992.0, "9007199254740992.0")]
    [InlineData(1.5e300, "1.5e+300")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(1234567890123456.0, "1234567890123456.0")]
    public void ToString_Fraction_PrintsTheShortestRoundTripTheWayTheDrawingsAlwaysHave(double value, string expected)
    {
        ((Num)value).ToString().Should().Be(expected);
    }

    [Fact]
    public void ToString_NegativeZero_KeepsItsSign() => ((Num)(-0.0)).ToString().Should().Be("-0.0");

    [Fact]
    public void ToString_PositiveZero_IsAFraction() => ((Num)0.0).ToString().Should().Be("0.0");

    [Fact]
    public void ToString_WholeNumber_HasNoPoint() => ((Num)40).ToString().Should().Be("40");

    [Fact]
    public void ToString_NotFinite_Throws()
    {
        var act = () => ((Num)double.NaN).ToString();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Arithmetic_WholeNumbers_StayWholeUnderPlusMinusTimes()
    {
        ((Num)3 + 4).ToString().Should().Be("7");
        ((Num)3 - 4).ToString().Should().Be("-1");
        ((Num)3 * 4).ToString().Should().Be("12");
        (-(Num)3).ToString().Should().Be("-3");
    }

    [Fact]
    public void Arithmetic_DivisionOrAFraction_GivesAFraction()
    {
        ((Num)4 / 2).ToString().Should().Be("2.0");
        ((Num)4 + 0.5).ToString().Should().Be("4.5");
        ((Num)2 * 1.5).IsFraction.Should().BeTrue();
        (-(Num)1.5).ToString().Should().Be("-1.5");
    }

    [Fact]
    public void Comparison_ComparesByValueWhateverTheKind()
    {
        ((Num)2 == 2.0).Should().BeTrue();
        ((Num)2 != 3).Should().BeTrue();
        ((Num)2 < 3).Should().BeTrue();
        ((Num)3 > 2).Should().BeTrue();
        ((Num)2 <= 2.0).Should().BeTrue();
        ((Num)2 >= 2.0).Should().BeTrue();
        ((Num)2).Equals((object)(Num)2.0).Should().BeTrue();
        ((Num)2).Equals("2").Should().BeFalse();
        ((Num)2).GetHashCode().Should().Be(((Num)2.0).GetHashCode());
        ((Num)2).CompareTo(3).Should().BeNegative();
    }

    [Fact]
    public void Max_Tie_KeepsTheFirstSoItsKindSurvives()
    {
        Num.Max(94, 94.0).ToString().Should().Be("94");
        Num.Max(94.0, 94).ToString().Should().Be("94.0");
        Num.Min(94, 94.0).ToString().Should().Be("94");
        Num.Min(94.0, 94).ToString().Should().Be("94.0");
        Num.Max(1, 2).ToString().Should().Be("2");
        Num.Min(1, 2).ToString().Should().Be("1");
    }

    [Fact]
    public void MaxMin_OverASequence_PickTheFirstExtreme()
    {
        Num.Max([(Num)1, 3.0, 3]).ToString().Should().Be("3.0");
        Num.Min([(Num)5, 2.0, 2]).ToString().Should().Be("2.0");
    }

    [Fact]
    public void Sum_OfNothing_IsWholeZero() => Num.Sum([]).ToString().Should().Be("0");

    [Fact]
    public void Sum_WithAFraction_IsAFraction() => Num.Sum([(Num)1, 0.5]).ToString().Should().Be("1.5");

    [Fact]
    public void Abs_KeepsTheKind()
    {
        Num.Abs(-3).ToString().Should().Be("3");
        Num.Abs(-3.5).ToString().Should().Be("3.5");
    }

    [Fact]
    public void Truncate_GivesAWholeNumberTowardsZero()
    {
        ((Num)2.9).Truncate().ToString().Should().Be("2");
        ((Num)(-2.9)).Truncate().ToString().Should().Be("-2");
    }

    [Fact]
    public void Value_IsTheDouble() => ((Num)3).Value.Should().Be(3.0);
}

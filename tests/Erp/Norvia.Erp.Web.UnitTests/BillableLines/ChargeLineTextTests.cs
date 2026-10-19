using Norvia.Erp.Web.BillableLines;

namespace Norvia.Erp.Web.UnitTests.BillableLines;

public sealed class ChargeLineTextTests
{
    [Theory]
    [InlineData("Freight", "Freight")]
    [InlineData("MinimumChargeAdjustment", "Minimum charge adjustment")]
    [InlineData("FuelSurcharge2", "Fuel surcharge2")]
    [InlineData("", "")]
    public void Of_WordsAChargeLineType_AsOneSentenceCasedPhrase(string type, string expected) =>
        ChargeLineText.Of(type).Should().Be(expected);

    [Fact]
    public void Of_WithoutAType_Throws()
    {
        var act = () => ChargeLineText.Of(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class RatingTextTests
{
    [Theory]
    [InlineData(RatingResult.NotYetRated, "Not yet rated")]
    [InlineData(RatingResult.Rated, "Rated")]
    [InlineData(RatingResult.RatingFailed, "Rating failed")]
    [InlineData(RatingResult.NotRatedSeparately, "Not rated separately")]
    public void Of_ARatingResult_NamesItAsTheGlossaryDoes(RatingResult result, string expected)
    {
        RatingText.Of(result).Should().Be(expected);
    }

    [Fact]
    public void Of_NoRatingResult_IsEmpty()
    {
        RatingText.Of(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Freight", "Freight")]
    [InlineData("MinimumChargeAdjustment", "Minimum charge adjustment")]
    [InlineData("FuelSurcharge", "FuelSurcharge")]
    public void ChargeLine_AChargeLineType_NamesItInPlainWords_OrAsItCameWhenItIsNew(string type, string expected)
    {
        RatingText.ChargeLine(type).Should().Be(expected);
    }

    [Theory]
    [InlineData(92.87, "EUR 92.87")]
    [InlineData(65, "EUR 65.00")]
    [InlineData(1234.5, "EUR 1,234.50")]
    public void Euros_AnAmount_ShowsItInEurWithCents(decimal amount, string expected)
    {
        RatingText.Euros(amount).Should().Be(expected);
    }
}

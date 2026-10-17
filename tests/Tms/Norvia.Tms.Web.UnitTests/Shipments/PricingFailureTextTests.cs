using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class PricingFailureTextTests
{
    [Theory]
    [InlineData("NoRateCard", "No Carrier Rate Card for this Carrier. Carrier: Kessler Spedition.")]
    [InlineData("NoValidRateCardVersion", "The Carrier Rate Card has no valid version for this date. Carrier: Kessler Spedition.")]
    [InlineData("NoMatchingLane", "The Carrier Rate Card has no Lane for this origin and destination. Carrier: Kessler Spedition.")]
    [InlineData("WeightAboveHighestBreak", "The weight is above the highest Weight Break on the Lane. Carrier: Kessler Spedition.")]
    public void Of_AKnownCode_IsOnePlainSentenceFollowedByTheCarrier(string code, string expected)
    {
        var text = PricingFailureText.Of(new PricingFailureView(code, "message"), Samples.ToRelease().CarriedBy);

        text.Should().Be(expected);
    }

    [Fact]
    public void Of_AnUnknownCode_FallsBackToTheMessageTheTmsSent()
    {
        var text = PricingFailureText.Of(new PricingFailureView("SomethingNew", "Kessler Spedition can't price it."), Samples.ToRelease().CarriedBy);

        text.Should().Be("Kessler Spedition can't price it.");
    }
}

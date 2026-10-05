using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

public sealed class TrackingModeTextTests
{
    [Theory]
    [InlineData(TrackingMode.Manual, "Manual")]
    [InlineData(TrackingMode.AutoProgress, "Auto-progress")]
    public void Of_GivesEachModeItsWords(TrackingMode mode, string words) => TrackingModeText.Of(mode).Should().Be(words);

    [Fact]
    public void Of_AnUnknownMode_Throws() => ((Action)(() => TrackingModeText.Of((TrackingMode)99))).Should().Throw<ArgumentOutOfRangeException>();
}

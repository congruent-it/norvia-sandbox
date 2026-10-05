using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

public sealed class TrackingEventTrackingTests
{
    private static readonly CarrierTrackingEvent Sent = new("evt-1", "VDT-1", "CB-0000001", TrackingEventType.Collected, Samples.Now, "Rotterdam", "3087 BM", "NLD");

    [Fact]
    public void KeysOf_AreTheTrackingContractsKeys_WithoutWhatTheEventSaid()
    {
        var keys = TrackingEventTracking.KeysOf(Samples.VanDijkTransport, "SH-0000042", Sent, "corr-1");

        keys.Should().Equal(new Dictionary<string, object?>
        {
            ["flow"] = "carrier-tracking",
            ["partyId"] = "CAR-0001",
            ["carrierBookingNumber"] = "CB-0000001",
            ["shipmentNumber"] = "SH-0000042",
            ["sourceMessageId"] = "evt-1",
            ["correlationId"] = "corr-1",
        });
    }

    [Fact]
    public void KeysOf_NorviaNeverReplied_LeavesTheCorrelationIdOut() =>
        TrackingEventTracking.KeysOf(Samples.VanDijkTransport, "SH-0000042", Sent, correlationId: null).Should().NotContainKey("correlationId");

    [Theory]
    [InlineData(TrackingResult.Accepted, "Succeeded", null)]
    [InlineData(TrackingResult.Refused, "Refused", null)]
    [InlineData(TrackingResult.NotReceived, "Failed", "TrackingNotReceived")]
    public void OutcomeOf_ReadsTheReply(TrackingResult result, string outcome, string? failureCode)
    {
        var keys = TrackingEventTracking.OutcomeOf(result);

        keys["outcome"].Should().Be(outcome);
        keys.GetValueOrDefault("failureCode").Should().Be(failureCode);
    }
}

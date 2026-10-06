using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

public sealed class ProofOfDeliveryTrackingTests
{
    private static readonly CarrierProofOfDelivery Sent = new(
        "doc-1", "VDT-1", "CB-0000001", "J. Jansen", Samples.Now, "Utrecht", "3511 AW", "NLD", ProofOfDeliveryCondition.Clean, null, []);

    [Fact]
    public void KeysOf_AreTheTrackingContractsKeys_WithoutWhatTheProofSaid()
    {
        var keys = ProofOfDeliveryTracking.KeysOf(Samples.VanDijkTransport, "SH-0000042", Sent, "corr-1");

        keys.Should().Equal(new Dictionary<string, object?>
        {
            ["flow"] = "carrier-proof-of-delivery",
            ["partyId"] = "CAR-0001",
            ["carrierBookingNumber"] = "CB-0000001",
            ["shipmentNumber"] = "SH-0000042",
            ["sourceMessageId"] = "doc-1",
            ["correlationId"] = "corr-1",
        });
    }

    [Fact]
    public void KeysOf_NorviaNeverReplied_LeavesTheCorrelationIdOut() =>
        ProofOfDeliveryTracking.KeysOf(Samples.VanDijkTransport, "SH-0000042", Sent, correlationId: null).Should().NotContainKey("correlationId");

    [Theory]
    [InlineData(TrackingResult.Accepted, "Succeeded", null)]
    [InlineData(TrackingResult.Refused, "Refused", null)]
    [InlineData(TrackingResult.NotReceived, "Failed", "ProofOfDeliveryNotReceived")]
    public void OutcomeOf_ReadsTheReply(TrackingResult result, string outcome, string? failureCode)
    {
        var keys = ProofOfDeliveryTracking.OutcomeOf(result);

        keys["outcome"].Should().Be(outcome);
        keys.GetValueOrDefault("failureCode").Should().Be(failureCode);
    }
}

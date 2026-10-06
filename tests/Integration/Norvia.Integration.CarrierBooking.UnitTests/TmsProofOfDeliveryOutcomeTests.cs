namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class TmsProofOfDeliveryOutcomeTests
{
    [Fact]
    public void Of_ARecordedProofOfDelivery_CompletesTheMessage_AndNamesTheShipment()
    {
        var outcome = TmsProofOfDeliveryOutcome.Of(200, """{"shipmentNumber":"SH-0000042","isRepeat":false}""");

        outcome.Should().BeEquivalentTo(new TmsProofOfDeliveryOutcome(ProofOfDeliverySettlement.Complete, "Succeeded", null, null, "SH-0000042"));
    }

    [Fact]
    public void Of_ARefusedProofOfDelivery_DeadLettersWithTheProblemsCodeAndDetail_WithoutRetry()
    {
        var outcome = TmsProofOfDeliveryOutcome.Of(409, """{"code":"ShipmentNotInTransport","detail":"Shipment SH-1 is Planned, not Booked or later."}""");

        outcome.Should().BeEquivalentTo(new TmsProofOfDeliveryOutcome(
            ProofOfDeliverySettlement.DeadLetter, "DeadLettered", "ShipmentNotInTransport", "Shipment SH-1 is Planned, not Booked or later.", null));
    }

    [Theory]
    [InlineData(400, "")]
    [InlineData(422, "not json")]
    [InlineData(404, """{"title":"Nope"}""")]
    public void Of_AClientErrorWithoutACode_DeadLettersAsTmsCallRefused(int status, string body)
    {
        var outcome = TmsProofOfDeliveryOutcome.Of(status, body);

        outcome.Settlement.Should().Be(ProofOfDeliverySettlement.DeadLetter);
        outcome.FailureCode.Should().Be("TmsCallRefused");
        outcome.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Of_AServerErrorOrTimeout_RetriesFromTheQueue(int status)
    {
        var outcome = TmsProofOfDeliveryOutcome.Of(status, string.Empty);

        outcome.Should().BeEquivalentTo(new TmsProofOfDeliveryOutcome(ProofOfDeliverySettlement.Retry, "Retrying", "TmsUnavailable", outcome.Description, null));
        outcome.Description.Should().NotBeNullOrWhiteSpace();
    }
}

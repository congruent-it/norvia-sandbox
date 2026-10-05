namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class TmsTrackingOutcomeTests
{
    [Fact]
    public void Of_ARecordedEvent_CompletesTheMessage_AndNamesTheShipment()
    {
        var outcome = TmsTrackingOutcome.Of(200, """{"shipmentNumber":"SH-0000042","eventId":"E1"}""");

        outcome.Should().BeEquivalentTo(new TmsTrackingOutcome(TrackingSettlement.Complete, "Succeeded", null, null, "SH-0000042"));
    }

    [Fact]
    public void Of_ARefusedEvent_DeadLettersWithTheProblemsCodeAndDetail_WithoutRetry()
    {
        var outcome = TmsTrackingOutcome.Of(404, """{"code":"ShipmentNotFound","detail":"No Shipment has that booking reference.","correlationId":"c"}""");

        outcome.Should().BeEquivalentTo(new TmsTrackingOutcome(TrackingSettlement.DeadLetter, "DeadLettered", "ShipmentNotFound", "No Shipment has that booking reference.", null));
    }

    [Theory]
    [InlineData(400, "")]
    [InlineData(422, "not json")]
    [InlineData(409, """{"title":"Nope"}""")]
    public void Of_AClientErrorWithoutACode_DeadLettersAsTmsCallRefused(int status, string body)
    {
        var outcome = TmsTrackingOutcome.Of(status, body);

        outcome.Settlement.Should().Be(TrackingSettlement.DeadLetter);
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
        var outcome = TmsTrackingOutcome.Of(status, string.Empty);

        outcome.Should().BeEquivalentTo(new TmsTrackingOutcome(TrackingSettlement.Retry, "Retrying", "TmsUnavailable", outcome.Description, null));
        outcome.Description.Should().NotBeNullOrWhiteSpace();
    }
}

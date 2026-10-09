using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.TrackingEvents.TrackingEventCalls;

namespace Norvia.Tms.Api.IntegrationTests.TrackingEvents;

/// <summary>
/// A Carrier can report the whole journey, or skip steps, and the Shipment's status only moves forward (P5): late events
/// and events after Delivered are logged and never move it back, one event is published per move, and an event ID sent
/// again is harmless.
/// </summary>
[Trait("AppHost", "Own1")]
public sealed class TrackingEventProgressTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private static readonly (string Type, int HoursAgo)[] Journey = [("Collected", 6), ("Departed", 5), ("Arrived", 4), ("OutForDelivery", 3), ("Delivered", 2)];
    private static readonly string[] InTransitTypes = ["Departed", "Arrived", "OutForDelivery", "Delivered"];
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public void Dispose() => tms.ResetClock();

    [Theory]
    [InlineData("Departed")]
    [InlineData("Arrived")]
    [InlineData("OutForDelivery")]
    public async Task Record_AnInTransitEventOnACollectedShipment_MovesItToInTransit_AndPublishesOneShipmentInTransit(string type)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        await tms.RecordedAsync(Collected(reference, _now.AddHours(-3)), _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Event(type, reference, _now.AddHours(-2)), _cancellationToken, correlationId);

        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("InTransit");
        recorded["movedShipment"]!.GetValue<bool>().Should().BeTrue();
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("InTransit");
        var message = (await tms.PublishedAsync(correlationId, "ShipmentInTransit", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentInTransit(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
    }

    [Theory]
    [InlineData("Departed")]
    [InlineData("Arrived")]
    [InlineData("OutForDelivery")]
    public async Task Record_AnInTransitEventStraightAfterBooked_MovesItToInTransit_AndPublishesOnlyShipmentInTransit(string type)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Event(type, reference, _now.AddMinutes(-1)), _cancellationToken, correlationId);

        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("InTransit");
        await tms.PublishedAsync(correlationId, "ShipmentInTransit", _cancellationToken);
        tms.Published.Where(m => CarrierBookingCalls.NamesShipment(m, shipmentNumber) && m.Subject == "ShipmentCollected")
            .Should().BeEmpty("the step skipped publishes nothing");
    }

    [Fact]
    public async Task Record_TheWholeJourney_PublishesOneEventPerMove_AndDeliveredIsFinal()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationIds = new List<string>();
        foreach (var (type, hoursAgo) in Journey)
        {
            var correlationId = TmsCalls.NewCorrelationId();
            correlationIds.Add(correlationId);
            await tms.RecordedAsync(Event(type, reference, _now.AddHours(-hoursAgo)), _cancellationToken, correlationId);
        }

        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Delivered");
        await tms.PublishedAsync(correlationIds[4], "ShipmentDelivered", _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        var published = correlationIds.Select(id => tms.Published.Where(m => m.CorrelationId == id).Select(m => m.Subject).ToArray()).ToArray();
        published.Select(subjects => string.Join(',', subjects.Order())).Should().Equal(
            "ShipmentCollected", "ShipmentInTransit", string.Empty, string.Empty, "CarrierCostIncurred,ShipmentDelivered");
    }

    [Fact]
    public async Task Record_DeliveredStraightAfterBooked_PublishesOnlyShipmentDelivered()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Event("Delivered", reference, _now.AddMinutes(-1)), _cancellationToken, correlationId);

        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        var message = (await tms.PublishedAsync(correlationId, "ShipmentDelivered", _cancellationToken)).Should().ContainSingle().Subject;
        var results = CanonicalModelSchemas.ValidateShipmentDelivered(JsonNode.Parse(message.Body.ToString())!);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        tms.Published.Where(m => CarrierBookingCalls.NamesShipment(m, shipmentNumber) && m.Subject is "ShipmentCollected" or "ShipmentInTransit")
            .Should().BeEmpty("the steps skipped publish nothing");
    }

    [Fact]
    public async Task Record_ALateEventWhoseStatusIsNotFurtherAlong_IsStored_ButMovesAndPublishesNothing()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        await tms.RecordedAsync(Event("OutForDelivery", reference, _now.AddHours(-1)), _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Collected(reference, _now.AddHours(-5), eventId: "evt-late"), _cancellationToken, correlationId);

        recorded["repeat"]!.GetValue<bool>().Should().BeFalse();
        recorded["movedShipment"]!.GetValue<bool>().Should().BeFalse();
        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("InTransit");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("InTransit");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == correlationId).Should().BeEmpty();
        // It was stored: sent again, it is a repeat.
        var again = await tms.RecordedAsync(Collected(reference, _now.AddHours(-5), eventId: "evt-late"), _cancellationToken);
        again["repeat"]!.GetValue<bool>().Should().BeTrue();
        again["correlationId"]!.GetValue<string>().Should().Be(correlationId);
    }

    [Fact]
    public async Task Record_AnEventAfterDelivered_IsAccepted_WithNoStatusChangeAndNothingPublished()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        await tms.RecordedAsync(Event("Delivered", reference, _now.AddHours(-2)), _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Event("Departed", reference, _now.AddHours(-1), eventId: "evt-after"), _cancellationToken, correlationId);

        recorded["movedShipment"]!.GetValue<bool>().Should().BeFalse();
        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Delivered");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == correlationId).Should().BeEmpty();
    }

    [Fact]
    public async Task Record_TheSameEventIdAgain_AfterItMovedTheShipmentOnward_ReturnsTheOriginalResultAsARepeat()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        var departed = Event("Departed", reference, _now.AddHours(-2), eventId: "evt-dep");
        await tms.RecordedAsync(departed, _cancellationToken, correlationId);
        await tms.RecordedAsync(Event("Delivered", reference, _now.AddHours(-1)), _cancellationToken);

        var repeated = await tms.RecordedAsync(departed, _cancellationToken);

        repeated["repeat"]!.GetValue<bool>().Should().BeTrue();
        repeated["movedShipment"]!.GetValue<bool>().Should().BeTrue();
        repeated["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "ShipmentInTransit" && CarrierBookingCalls.NamesShipment(m, shipmentNumber)).Should().Be(1);
    }

    [Fact]
    public void TheEventsTheTestsSend_AreTheCanonicalModelsTrackingEvent()
    {
        foreach (var type in InTransitTypes)
        {
            var results = CanonicalModelSchemas.ValidateTrackingEvent(Event(type, "VDT-1", _now));
            results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        }
    }
}

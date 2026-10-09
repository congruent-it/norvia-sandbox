using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Billing;

/// <summary>
/// When a Carrier Shipment moves to Delivered (P5), the same transaction publishes CarrierCostIncurred for the Confirmed
/// Carrier booking that carried it, without waiting for a Proof of delivery or for Billable (P9). Only the Delivered move
/// publishes it, once, and only for a Confirmed booking.
/// </summary>
public sealed class CarrierCostIncurredTests(TmsFixture tms)
{
    private const string Remark = "Agreed with Proef Transport by phone, 480 euro";

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delivered_PublishesOneCarrierCostIncurred_OnTheDeliveredEventsTrail_WithTheFrozenCarrierCostAndTheBookingsFacts()
    {
        var (shipmentNumber, bookingNumber, reference) = await tms.BookedShipmentWithNumberAsync(_cancellationToken);
        var booking = CarrierBookingCalls.BookingOf(await tms.GetShipmentAsync(shipmentNumber, _cancellationToken), bookingNumber);
        var transportOrderNumber = (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["transportOrderNumber"]!.GetValue<string>();
        var correlationId = TmsCalls.NewCorrelationId();
        var deliveredAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());

        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, deliveredAt), _cancellationToken, correlationId);

        var message = (await tms.PublishedAsync(correlationId, "CarrierCostIncurred", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateCarrierCostIncurred(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(bookingNumber);
        body["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(transportOrderNumber);
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId, "it carries the Delivered Tracking event's correlation ID");
        JsonNode.DeepEquals(body["carrierCost"], booking["carrierCost"]).Should().BeTrue("the Carrier cost is the one frozen on the booking at release");
        body["carrierCost"]!["source"]!["kind"]!.GetValue<string>().Should().Be("CarrierRateCard");
        DateTimeOffset.Parse(body["deliveryTime"]!.GetValue<string>(), CultureInfo.InvariantCulture).Should().Be(deliveredAt);
        message.CorrelationId.Should().Be(correlationId);
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["flow"] = "carrier-tracking",
            ["partyId"] = PlanningCalls.CarrierId,
            ["transportOrderNumber"] = transportOrderNumber,
            ["shipmentNumber"] = shipmentNumber,
            ["carrierBookingNumber"] = bookingNumber,
            ["assignedTo"] = "Carrier",
        });
    }

    [Fact]
    public async Task Delivered_ForABookingReleasedWithASpotPrice_PublishesCarrierCostIncurred_WithSourceSpotAndItsRemark()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredTo("9711 AA"), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingToSettleAsync(number, _cancellationToken);
        var shipmentNumber = (await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken))["shipmentNumber"]!.GetValue<string>();
        using var release = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, Remark), _cancellationToken);
        var released = JsonNode.Parse(await release.Content.ReadAsStringAsync(_cancellationToken))!;
        var reference = CarrierBookingCalls.NewReference();
        await tms.ConfirmedAsync(released["carrierBookingNumber"]!.GetValue<string>(), PlanningCalls.CarrierId, reference, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken, correlationId);

        var message = (await tms.PublishedAsync(correlationId, "CarrierCostIncurred", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateCarrierCostIncurred(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["carrierCost"]!["total"]!.GetValue<decimal>().Should().Be(480m);
        body["carrierCost"]!["source"]!["kind"]!.GetValue<string>().Should().Be("Spot");
        body["carrierCost"]!["source"]!["remark"]!.GetValue<string>().Should().Be(Remark);
    }

    [Fact]
    public async Task Delivered_DoesNotWaitForAProofOfDeliveryOrBillable()
    {
        var (shipmentNumber, _, reference) = await tms.BookedShipmentWithNumberAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken, correlationId);

        await tms.PublishedAsync(correlationId, "CarrierCostIncurred", _cancellationToken);
        (await tms.GetTrackingTimelineAsync(shipmentNumber, _cancellationToken)).Should().NotBeEmpty();
        tms.Published.Where(m => m.Subject == "ProofOfDeliveryReceived" && NamesShipment(m, shipmentNumber)).Should().BeEmpty("no Proof of delivery was recorded");
    }

    [Fact]
    public async Task OtherMovesAndRepeats_PublishNoCarrierCostIncurred()
    {
        var (shipmentNumber, _, reference) = await tms.BookedShipmentWithNumberAsync(_cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await tms.RecordedAsync(TrackingEventCalls.Event("Collected", reference, now.AddMinutes(-30)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Departed", reference, now.AddMinutes(-20)), _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        CostIncurredFor(shipmentNumber).Should().BeEmpty("the Shipment isn't Delivered yet");

        var correlationId = TmsCalls.NewCorrelationId();
        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, now.AddMinutes(-5)), _cancellationToken, correlationId);
        await tms.PublishedAsync(correlationId, "CarrierCostIncurred", _cancellationToken);

        // A Delivered event sent again under a new ID, and one sent again under the same ID.
        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, now.AddMinutes(-4)), _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        CostIncurredFor(shipmentNumber).Should().ContainSingle("the Carrier cost is incurred once, when the Shipment moves to Delivered");
    }

    [Fact]
    public async Task RejectedBooking_NeverPublishesCarrierCostIncurred()
    {
        var (bookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.RejectedAsync(bookingNumber, PlanningCalls.CarrierId, "PriceNotAccepted", _cancellationToken);

        await tms.RecordRefusedAsync(
            TrackingEventCalls.Event("Delivered", "NO-CONFIRMED-BOOKING", DateTimeOffset.UtcNow.AddMinutes(-1)), HttpStatusCode.NotFound, _cancellationToken);

        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        CostIncurredFor(shipmentNumber).Should().BeEmpty();
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Planned");
    }

    [Fact]
    public async Task RefusedDeliveredEvent_PublishesNoCarrierCostIncurred()
    {
        var (shipmentNumber, _, reference) = await tms.BookedShipmentWithNumberAsync(_cancellationToken);

        await tms.RecordRefusedAsync(
            TrackingEventCalls.Event("Delivered", reference, DateTimeOffset.UtcNow.AddDays(3)), HttpStatusCode.BadRequest, _cancellationToken);

        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        CostIncurredFor(shipmentNumber).Should().BeEmpty("the change didn't commit, so nothing is published");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Booked");
    }

    private IReadOnlyList<ServiceBusReceivedMessage> CostIncurredFor(string shipmentNumber) =>
        [.. tms.Published.Where(m => m.Subject == "CarrierCostIncurred" && NamesShipment(m, shipmentNumber))];

    private static bool NamesShipment(ServiceBusReceivedMessage message, string shipmentNumber) =>
        message.ApplicationProperties.TryGetValue("shipmentNumber", out var number) && Equals(number, shipmentNumber);
}

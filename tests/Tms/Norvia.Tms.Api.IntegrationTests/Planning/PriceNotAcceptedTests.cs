using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>
/// A Carrier that doesn't accept the Carrier cost rejects the booking with the reason code PriceNotAccepted (P9). The
/// Shipment goes back to Planned as for any rejection, and the Planner can release it again, which creates a new Carrier
/// booking with a Carrier cost of its own. There is no counter-offer.
/// </summary>
public sealed class PriceNotAcceptedTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reject_WithPriceNotAccepted_RejectsTheBooking_SendsTheShipmentBackToPlanned_AndPublishesTheReasonCode()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var rejected = await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "PriceNotAccepted", _cancellationToken);

        rejected["status"]!.GetValue<string>().Should().Be("Rejected");
        rejected["shipmentStatus"]!.GetValue<string>().Should().Be("Planned");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned");
        var booking = CarrierBookingCalls.BookingOf(shipment, carrierBookingNumber);
        booking["status"]!.GetValue<string>().Should().Be("Rejected");
        booking["reasonCode"]!.GetValue<string>().Should().Be("PriceNotAccepted");
        var events = await tms.PublishedAsync(rejected["correlationId"]!.GetValue<string>(), "CarrierBookingRejected", _cancellationToken);
        var body = JsonNode.Parse(events.Should().ContainSingle().Subject.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateCarrierBookingRejected(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["reasonCode"]!.GetValue<string>().Should().Be("PriceNotAccepted");
    }

    [Fact]
    public async Task Release_AfterAPriceNotAcceptedRejection_CreatesANewBookingWithItsOwnNewlyCalculatedCarrierCost()
    {
        var (first, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.RejectedAsync(first, PlanningCalls.CarrierId, "PriceNotAccepted", _cancellationToken);

        var second = (await tms.ReleasedAsync(shipmentNumber, _cancellationToken))["carrierBookingNumber"]!.GetValue<string>();

        second.Should().NotBe(first);
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Released");
        CarrierBookingCalls.BookingOf(shipment, first)["status"]!.GetValue<string>().Should().Be("Rejected");
        var newBooking = CarrierBookingCalls.BookingOf(shipment, second);
        newBooking["status"]!.GetValue<string>().Should().Be("Requested");
        newBooking["carrierCost"]!["total"]!.GetValue<decimal>().Should().Be(86.50m);
        newBooking["carrierCost"]!["calculatedAt"]!.GetValue<DateTimeOffset>()
            .Should().BeAfter(CarrierBookingCalls.BookingOf(shipment, first)["carrierCost"]!["calculatedAt"]!.GetValue<DateTimeOffset>());
    }

    [Fact]
    public async Task Timeline_ShowsTheRejectedBookingWithItsReasonCode_AndTheNewBookingBesideIt()
    {
        var (first, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.RejectedAsync(first, PlanningCalls.CarrierId, "PriceNotAccepted", _cancellationToken);
        var second = (await tms.ReleasedAsync(shipmentNumber, _cancellationToken))["carrierBookingNumber"]!.GetValue<string>();

        var timeline = await tms.GetTimelineAsync(shipmentNumber, _cancellationToken);

        var bookings = timeline["carrierBookings"]!.AsArray();
        bookings.Select(booking => booking!["carrierBookingNumber"]!.GetValue<string>()).Should().Equal(first, second);
        bookings[0]!["status"]!.GetValue<string>().Should().Be("Rejected");
        bookings[0]!["reasonCode"]!.GetValue<string>().Should().Be("PriceNotAccepted");
        bookings[1]!["status"]!.GetValue<string>().Should().Be("Requested");
    }
}

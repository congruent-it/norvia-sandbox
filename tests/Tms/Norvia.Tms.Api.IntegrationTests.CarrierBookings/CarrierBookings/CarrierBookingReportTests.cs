using System.Net;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.CarrierBookings.CarrierBookingCalls;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// The P4 booking Logic App reports a Requested Carrier booking as sent or not delivered (P4.4). A report for a booking
/// that is no longer Requested is ignored and logged: it changes and publishes nothing.
/// </summary>
public sealed class CarrierBookingReportTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReportSent_ARequestedBooking_RecordsTheSentTime_AndPublishesNothing()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var reported = await tms.ReportedSentAsync(carrierBookingNumber, _cancellationToken);

        reported["status"]!.GetValue<string>().Should().Be("Requested");
        reported["ignored"]!.GetValue<bool>().Should().BeFalse();
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        BookingOf(shipment, carrierBookingNumber)["sentAt"].Should().NotBeNull();
        tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber)).Should().Be(1, "only ShipmentReleased names it; a sent report publishes nothing");
    }

    [Fact]
    public async Task ReportNotDelivered_ARequestedBooking_MovesItToNotDelivered_AndTheShipmentToPlanned_AndPublishesOneCarrierBookingNotDelivered()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var correlationId = Guid.NewGuid().ToString();

        var reported = await tms.ReportedNotDeliveredAsync(carrierBookingNumber, _cancellationToken, 503, "Service unavailable.", correlationId);

        reported["status"]!.GetValue<string>().Should().Be("NotDelivered");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned");
        var booking = BookingOf(shipment, carrierBookingNumber);
        booking["status"]!.GetValue<string>().Should().Be("NotDelivered");

        var events = await tms.PublishedAsync(correlationId, "CarrierBookingNotDelivered", _cancellationToken);
        var message = events.Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateCarrierBookingNotDelivered(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Service unavailable.");
    }

    [Fact]
    public async Task ReportSent_ABookingWithdrawnWhileTheReportWasInFlight_IsIgnored_AndChangesNothing()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var withdrawn = await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);
        await tms.PublishedAsync(withdrawn["correlationId"]!.GetValue<string>(), "CarrierBookingWithdrawn", _cancellationToken); // The withdrawal's event is out before the count.
        var before = tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber));

        var reported = await tms.ReportedSentAsync(carrierBookingNumber, _cancellationToken);

        reported["ignored"]!.GetValue<bool>().Should().BeTrue();
        reported["status"]!.GetValue<string>().Should().Be("Withdrawn");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        BookingOf(shipment, carrierBookingNumber)["sentAt"].Should().BeNull();
        tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber)).Should().Be(before);
    }

    [Fact]
    public async Task ReportNotDelivered_ABookingWithdrawnWhileTheReportWasInFlight_IsIgnored_AndChangesAndPublishesNothing()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var withdrawn = await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);
        await tms.PublishedAsync(withdrawn["correlationId"]!.GetValue<string>(), "CarrierBookingWithdrawn", _cancellationToken); // The withdrawal's event is out before the count.
        var before = tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber));

        var reported = await tms.ReportedNotDeliveredAsync(carrierBookingNumber, _cancellationToken);

        reported["ignored"]!.GetValue<bool>().Should().BeTrue();
        reported["status"]!.GetValue<string>().Should().Be("Withdrawn");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned", "the withdrawal already returned it, not this ignored report");
        tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber)).Should().Be(before);
    }

    [Fact]
    public async Task ReportSent_AnUnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        using var response = await tms.ReportSentAsync("CB-9999999", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReportNotDelivered_AnUnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        using var response = await tms.ReportNotDeliveredAsync("CB-9999999", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

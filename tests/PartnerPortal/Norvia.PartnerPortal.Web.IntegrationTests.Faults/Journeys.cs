using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.IntegrationTests.Faults;

/// <summary>A booking a test made for itself: the numbers the TMS gave it.</summary>
public sealed record BookedShipment(string TransportOrderNumber, string ShipmentNumber, string CarrierBookingNumber);

/// <summary>
/// The steps that bring a Shipment to where a fault meets it, through the estate's own doors: the Transport Order is taken in as the intake Logic App does, the Planner assigns and
/// releases it, the booking worker carries the request to the Carrier Persona's inbox, and the Persona answers and tracks as the portal's pages do. Every test makes a Shipment
/// of its own, so none depends on another's.
/// </summary>
public sealed class Journeys(FaultsFixture fixture)
{
    private static readonly PersonaId Carrier = new(FaultsFixture.CarrierPersona);

    /// <summary>A Shipment released to the Carrier: the booking is Requested, and the booking worker is on its way to the Carrier Persona's endpoint with it.</summary>
    public async Task<BookedShipment> ReleasedAsync(CancellationToken cancellationToken)
    {
        var transportOrder = await fixture.Tms.EnterTransportOrderAsync(FaultsFixture.ShipperId, $"FAULT-{Guid.NewGuid():N}", cancellationToken);
        var shipment = await fixture.Tms.AssignToCarrierAsync(transportOrder, FaultsFixture.CarrierId, cancellationToken);
        var booking = await fixture.Tms.ReleaseAsync(shipment, cancellationToken);
        return new BookedShipment(transportOrder, shipment, booking);
    }

    /// <summary>A Shipment released to the Carrier, with the booking request in the Carrier Persona's inbox, Open.</summary>
    public async Task<BookedShipment> RequestedAsync(CancellationToken cancellationToken)
    {
        var released = await ReleasedAsync(cancellationToken);
        await InboxHasAsync(released.CarrierBookingNumber, cancellationToken);
        return released;
    }

    /// <summary>A Shipment the Carrier Persona confirmed, so the TMS has it Booked.</summary>
    public async Task<BookedShipment> ConfirmedAsync(CancellationToken cancellationToken)
    {
        var requested = await RequestedAsync(cancellationToken);
        var answered = await fixture.Portal.AnswerCarrierBookingAsync(
            Carrier, requested.CarrierBookingNumber, new CarrierAnswer.Confirmation($"VDT-{Guid.NewGuid():N}"[..16]), cancellationToken);
        answered!.Reply!.IsRecorded.Should().BeTrue(answered.Reply.Text);
        await ShipmentIsAsync(requested, "Booked", cancellationToken);
        return requested;
    }

    /// <summary>A Shipment In transit: the Carrier Persona collected it and departed, and the TMS recorded both.</summary>
    public async Task<BookedShipment> InTransitAsync(CancellationToken cancellationToken)
    {
        var booked = await ConfirmedAsync(cancellationToken);
        await TrackedAsync(booked, TrackingEventType.Collected, "Collected", cancellationToken);
        await TrackedAsync(booked, TrackingEventType.Departed, "InTransit", cancellationToken);
        return booked;
    }

    /// <summary>A Shipment the Carrier Persona delivered: the TMS recorded the Delivered event.</summary>
    public async Task<BookedShipment> DeliveredAsync(CancellationToken cancellationToken)
    {
        var booked = await InTransitAsync(cancellationToken);
        await TrackedAsync(booked, TrackingEventType.Delivered, "Delivered", cancellationToken);
        return booked;
    }

    /// <summary>The Carrier Persona sends the event, which the Partner API accepts, and the TMS has the Shipment at the status once the tracking worker has recorded it.</summary>
    public async Task TrackedAsync(BookedShipment shipment, TrackingEventType type, string status, CancellationToken cancellationToken)
    {
        var sent = await fixture.Portal.SendTrackingEventAsync(Carrier, shipment.CarrierBookingNumber, type, cancellationToken);
        sent!.Reply.Result.Should().Be(TrackingResult.Accepted, sent.Reply.Text);
        await ShipmentIsAsync(shipment, status, cancellationToken);
    }

    public async Task ShipmentIsAsync(BookedShipment shipment, string status, CancellationToken cancellationToken) =>
        await Eventually.UntilAsync(async () => (await fixture.Tms.ShipmentAsync(shipment.ShipmentNumber, cancellationToken))["status"]!.GetValue<string>() == status, cancellationToken);

    /// <summary>The booking as the TMS holds it, once it has the status.</summary>
    public async Task<JsonObject> BookingAsync(BookedShipment shipment, string status, CancellationToken cancellationToken) =>
        await Eventually.Async(
            async () =>
            {
                var bookings = (await fixture.Tms.ShipmentAsync(shipment.ShipmentNumber, cancellationToken))["carrierBookings"]!.AsArray();
                return bookings.OfType<JsonObject>().SingleOrDefault(booking => booking["carrierBookingNumber"]!.GetValue<string>() == shipment.CarrierBookingNumber
                    && booking["status"]!.GetValue<string>() == status);
            },
            cancellationToken);

    private async Task InboxHasAsync(string carrierBookingNumber, CancellationToken cancellationToken) =>
        await Eventually.UntilAsync(
            async () => (await fixture.Portal.ListCarrierBookingRequestsAsync(Carrier, cancellationToken)).Any(request => request.CarrierBookingNumber == carrierBookingNumber),
            cancellationToken);
}

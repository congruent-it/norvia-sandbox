using System.Text.Json.Nodes;
using Norvia.Tms.TestSupport.CarrierBookings;
using Norvia.Tms.TestSupport.Planning;
using Norvia.Tms.TestSupport.TrackingEvents;

namespace Norvia.Tms.TestSupport.ConsigneeNotifications;

/// <summary>What the Consignee notification tests do at the TMS's boundary: book a Shipment whose Transport Order has, or has not, a Consignee email, and have the Carrier collect it.</summary>
internal static class ConsigneeNotificationCalls
{
    /// <summary>A booking as the intake Logic App sends it, whose Consignee has the email, or none when it is null.</summary>
    public static JsonObject BookingFor(string? consigneeEmail)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference());
        booking["delivery"]!["consignee"]!["email"] = consigneeEmail;
        return booking;
    }

    /// <summary>
    /// A Booked Carrier Shipment of a Transport Order the Consignee's email is on (or no email when it is null): the TMS accepts and rates the order, the Planner plans and releases
    /// it, and the Carrier confirms the booking. It is not Collected yet.
    /// </summary>
    public static async Task<(string ShipmentNumber, string TransportOrderNumber, string CarrierBookingReference)> BookedShipmentFor(
        this TmsFixture tms, string? consigneeEmail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        var transportOrderNumber = await tms.SubmitAcceptedAsync(BookingFor(consigneeEmail), TmsCalls.NewCorrelationId(), cancellationToken);
        await tms.WaitForRatingResultAsync(transportOrderNumber, "Rated", cancellationToken);

        var assigned = await tms.AssignPlannedAsync(transportOrderNumber, PlanningCalls.ToCarrier(), cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", cancellationToken);
        var shipmentNumber = assigned["shipmentNumber"]!.GetValue<string>();

        var released = await tms.ReleasedAsync(shipmentNumber, cancellationToken);
        await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", cancellationToken);

        var reference = CarrierBookingCalls.NewReference();
        await tms.ConfirmedAsync(released["carrierBookingNumber"]!.GetValue<string>(), PlanningCalls.CarrierId, reference, cancellationToken);
        return (shipmentNumber, transportOrderNumber, reference);
    }

    /// <summary>The Carrier reports the Shipment Collected, as the Carrier-tracking Logic App does, and the TMS accepts it. Returns the correlation ID of the report.</summary>
    public static async Task<string> CollectedAsync(this TmsFixture tms, string carrierBookingReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        var correlationId = TmsCalls.NewCorrelationId();
        await tms.RecordedAsync(TrackingEventCalls.Collected(carrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-2)), cancellationToken, correlationId);
        return correlationId;
    }
}

using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.Exceptions.Timeline;

/// <summary>The cancellation of a Carrier booking the Planner made from an Exception, on a Shipment's timeline (P7).</summary>
/// <param name="CarrierNotToldMessage">The technical cause when the Carrier wasn't told; null otherwise.</param>
public sealed record CarrierBookingCancellationView(
    string CarrierBookingNumber, string Reason, DateTimeOffset CancelledAt, bool CarrierNotTold, string? CarrierNotToldMessage)
{
    public static CarrierBookingCancellationView Of(CarrierBooking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(
            booking.Number.Value,
            booking.CancellationReasonText ?? string.Empty,
            booking.CancelledAt ?? throw new ArgumentException($"Carrier booking {booking.Number} wasn't cancelled.", nameof(booking)),
            booking.IsCarrierNotTold,
            booking.CarrierNotToldMessage);
    }
}

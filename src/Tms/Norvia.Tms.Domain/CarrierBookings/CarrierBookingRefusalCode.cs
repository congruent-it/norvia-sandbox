namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// Why the TMS refused a Carrier's answer (P4), for the rules that have a stable code (the design's answer rules table
/// gives the wrong-Carrier rule none, only "Refused": see <see cref="CarrierBookingRefusal"/>). The names are stable
/// codes that callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum CarrierBookingRefusalCode
{
    /// <summary>No Carrier booking has the number.</summary>
    BookingNotFound = 1,

    /// <summary>The Carrier booking isn't Requested, and the answer isn't a repeat of what's already recorded.</summary>
    BookingNotOpen,
}

using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.CarrierBookings;

/// <summary>TMS settings for Carrier bookings.</summary>
public sealed class CarrierBookingOptions
{
    public const string SectionName = "CarrierBooking";

    /// <summary>
    /// How long a Requested Carrier booking waits before it is flagged overdue in the Planner's list of bookings
    /// awaiting an answer, unless its Shipment's collection window starts sooner (P4).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "1.00:00:00")]
    public TimeSpan OverdueAfter { get; init; } = TimeSpan.FromHours(2);
}

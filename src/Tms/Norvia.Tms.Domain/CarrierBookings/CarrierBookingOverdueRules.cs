namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>P4's overdue rule for the Planner's list of bookings awaiting an answer.</summary>
public sealed class CarrierBookingOverdueRules(TimeSpan overdueAfter)
{
    /// <summary>
    /// A Requested booking is overdue once it has waited longer than the TMS's setting, or once its Shipment's
    /// collection window's earliest time has passed, whichever comes first. Overdue is only a flag: nothing acts on it.
    /// </summary>
    /// <param name="collectionWindowEarliest">The Shipment's Transport Order's collection window's earliest time, if it has one.</param>
    public bool IsOverdue(DateTimeOffset requestedAt, DateTimeOffset? collectionWindowEarliest, DateTimeOffset now) =>
        now - requestedAt >= overdueAfter || (collectionWindowEarliest is { } earliest && now >= earliest);
}

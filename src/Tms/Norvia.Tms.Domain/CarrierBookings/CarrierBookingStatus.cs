namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// P4's later tickets add Not delivered and Withdrawn (final, like Confirmed and Rejected), and P7 adds Cancelled.
/// </summary>
public enum CarrierBookingStatus
{
    /// <summary>Created at release. Waits for the Carrier's answer.</summary>
    Requested,

    /// <summary>The Carrier accepted it, with its Carrier booking reference. Final.</summary>
    Confirmed,

    /// <summary>The Carrier turned it down, with a reason. Final.</summary>
    Rejected,
}

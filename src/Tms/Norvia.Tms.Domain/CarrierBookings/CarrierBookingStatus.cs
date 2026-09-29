namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>P7 adds Cancelled.</summary>
public enum CarrierBookingStatus
{
    /// <summary>Created at release. Waits for the Carrier's answer.</summary>
    Requested,

    /// <summary>The Carrier accepted it, with its Carrier booking reference. Final.</summary>
    Confirmed,

    /// <summary>The Carrier turned it down, with a reason. Final.</summary>
    Rejected,

    /// <summary>The request never reached the Carrier, with the technical cause. Final.</summary>
    NotDelivered,

    /// <summary>The Planner took it back before the Carrier answered. Final.</summary>
    Withdrawn,
}

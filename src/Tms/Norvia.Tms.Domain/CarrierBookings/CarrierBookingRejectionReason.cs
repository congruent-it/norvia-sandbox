namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// Why a Carrier rejected a Carrier booking. The names are stable codes that callers and monitoring rely on: never
/// rename one, only add. <c>PriceNotAccepted</c> is P9's addition, deferred with the Carrier cost.
/// </summary>
public enum CarrierBookingRejectionReason
{
    /// <summary>The Carrier has no capacity for it.</summary>
    NoCapacity,

    /// <summary>The Carrier doesn't serve this origin and destination.</summary>
    LaneNotServed,

    /// <summary>The Carrier can't meet the collection or delivery window.</summary>
    WindowNotFeasible,

    /// <summary>The Carrier doesn't take these goods.</summary>
    GoodsNotAccepted,

    /// <summary>Anything else. A later ticket requires the reason text for this code.</summary>
    Other,
}

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>Why a Carrier rejects a booking request. <see cref="Other"/> needs a reason text.</summary>
public enum CarrierRejectionReason
{
    NoCapacity,
    LaneNotServed,
    WindowNotFeasible,
    GoodsNotAccepted,
    Other,
}

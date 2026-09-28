namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// Only <see cref="Requested"/> exists in this build. P4's later tickets add Confirmed, Rejected, Not delivered and
/// Withdrawn (each final), and P7 adds Cancelled.
/// </summary>
public enum CarrierBookingStatus
{
    /// <summary>Created at release. Waits for the Carrier's answer.</summary>
    Requested,
}

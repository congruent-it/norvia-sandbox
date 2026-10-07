namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>What Norvia tells a Carrier when it ends a booking.</summary>
public enum CarrierCancellationKind
{
    /// <summary>Norvia took back a request the Carrier hadn't answered.</summary>
    Withdrawn,

    /// <summary>Norvia ended a booking the Carrier had confirmed, with a reason.</summary>
    Cancelled,
}

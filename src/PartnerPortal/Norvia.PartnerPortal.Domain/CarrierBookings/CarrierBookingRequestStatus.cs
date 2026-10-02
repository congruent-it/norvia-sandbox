namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>Where a request in the Carrier's inbox stands.</summary>
public enum CarrierBookingRequestStatus
{
    /// <summary>Norvia asked and the Carrier hasn't answered.</summary>
    Open,

    /// <summary>The Carrier confirmed, and Norvia recorded it.</summary>
    Confirmed,

    /// <summary>The Carrier rejected, and Norvia recorded it.</summary>
    Rejected,

    /// <summary>Norvia answered a late answer with BookingNotOpen: Norvia withdrew the request, or closed it some other way, while the Carrier was answering.</summary>
    WithdrawnByNorvia,
}

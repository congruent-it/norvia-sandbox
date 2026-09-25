namespace Norvia.PartnerPortal.Domain.Bookings;

/// <summary>What Norvia made of a booking (contracts/partner-api/README.md, Responses).</summary>
public enum BookingResult
{
    /// <summary>Norvia took it in as a new Transport Order.</summary>
    Accepted,

    /// <summary>Norvia had already taken in the same booking, and answered with the original Transport Order.</summary>
    AlreadyBooked,

    /// <summary>The booking broke Norvia's rules; nothing was booked.</summary>
    Rejected,

    /// <summary>Norvia couldn't be reached or couldn't take it in; nothing was booked, so it should be sent again.</summary>
    NotReceived,

    /// <summary>Norvia refused the caller, for example a Carrier, which may not book Transport Orders.</summary>
    Refused,
}

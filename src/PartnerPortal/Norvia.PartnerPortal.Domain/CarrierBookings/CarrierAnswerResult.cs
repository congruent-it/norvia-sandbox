namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>What Norvia answered to a Carrier's answer, as the party outside Norvia got it.</summary>
public enum CarrierAnswerResult
{
    /// <summary>Norvia recorded the answer, or already had the same one.</summary>
    Recorded,

    /// <summary>Norvia no longer has the request open: it withdrew it while the Carrier was answering, or the request already has a different answer.</summary>
    BookingNotOpen,

    /// <summary>The confirmation's reference is one the Carrier already holds on another confirmed booking. Nothing was recorded, and the Carrier confirms again with another reference.</summary>
    ReferenceInUse,

    /// <summary>Norvia has no such booking.</summary>
    BookingNotFound,

    /// <summary>The booking isn't this Carrier's to answer.</summary>
    NotAllowed,

    /// <summary>Norvia can't use the answer. The portal's form checks it first, so this is for what the form let through.</summary>
    Invalid,

    /// <summary>Norvia couldn't take the answer in, or couldn't be reached, so nothing was recorded. Answering again is safe.</summary>
    NotReceived,

    /// <summary>The Norvia Partner API refused the Persona's key, token or role.</summary>
    Refused,
}

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>What the Norvia Partner API answered to a Tracking event or a Proof of delivery.</summary>
public enum TrackingResult
{
    /// <summary>API Management took the event in. Whether the TMS records it is for Norvia to see.</summary>
    Accepted,

    /// <summary>The Norvia Partner API refused the Persona's key, token or role.</summary>
    Refused,

    /// <summary>The event couldn't be put on its way, or Norvia couldn't be reached. Sending it again is safe.</summary>
    NotReceived,
}

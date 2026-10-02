namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>How a Carrier Persona answers the booking requests that reach its inbox.</summary>
public enum CarrierAnswerMode
{
    /// <summary>The presenter confirms or rejects each request by hand.</summary>
    Manual,

    /// <summary>The Persona confirms each request after its delay.</summary>
    AutoConfirm,

    /// <summary>The Persona rejects each request after its delay, with a chosen reason code.</summary>
    AutoReject,
}

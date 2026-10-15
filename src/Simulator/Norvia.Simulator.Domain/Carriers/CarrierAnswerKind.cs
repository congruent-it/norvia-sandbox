namespace Norvia.Simulator.Domain.Carriers;

/// <summary>How a Carrier meets a Carrier booking request: not asked yet, confirms, declines, or never answers.</summary>
public enum CarrierAnswerKind
{
    /// <summary>The Shipment wasn't released, so no request was made.</summary>
    None,

    Confirm,
    Decline,

    /// <summary>The Carrier was asked and says nothing; the booking stays Requested.</summary>
    NoAnswer,
}

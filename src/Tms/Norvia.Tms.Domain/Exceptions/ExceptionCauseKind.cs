namespace Norvia.Tms.Domain.Exceptions;

/// <summary>What kind of thing caused an Exception (P7). The names are stable: never rename one, only add.</summary>
public enum ExceptionCauseKind
{
    /// <summary>A Tracking event of the Shipment.</summary>
    TrackingEvent = 1,

    /// <summary>A Proof of delivery of the Shipment.</summary>
    ProofOfDelivery = 2,
}

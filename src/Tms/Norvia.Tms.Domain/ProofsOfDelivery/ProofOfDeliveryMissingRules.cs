namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>P6's missing rule for the Planner's list of Delivered Shipments without a Proof of delivery.</summary>
public sealed class ProofOfDeliveryMissingRules(TimeSpan missingAfter)
{
    /// <summary>
    /// A Delivered Shipment without a current Proof of delivery is flagged missing once the TMS's setting has passed
    /// since the event time of its Delivered Tracking event. It is only a flag: nothing acts on it and nothing is published.
    /// </summary>
    public bool IsMissing(DateTimeOffset deliveredEventTime, DateTimeOffset now) => now - deliveredEventTime >= missingAfter;
}

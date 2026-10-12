namespace Norvia.Tms.Domain.Trips;

/// <summary>What became of a Shipment on a Trip the Driver ended (P12).</summary>
public enum ShipmentTripResult
{
    /// <summary>Its delivery Stop is Delivered.</summary>
    Delivered,

    /// <summary>Collected, but its delivery failed: it is Awaiting re-delivery.</summary>
    NotDelivered,

    /// <summary>Its collection failed: it is Planned again.</summary>
    NotCollected,
}

namespace Norvia.Tms.Domain.Trips;

public enum StopKind
{
    /// <summary>Collecting the Shipment's goods at its collection place.</summary>
    Collection = 1,

    /// <summary>Delivering the Shipment's goods to its Consignee.</summary>
    Delivery,
}

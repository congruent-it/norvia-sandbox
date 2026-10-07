namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>
/// What a Shipper sees of a Transport Order's progress (P8). It is worked out from the Transport Order and its Shipment
/// when asked and never stored (ADR 0011). The names are stable: never rename one, only add.
/// </summary>
public enum TrackingStatus
{
    /// <summary>The Transport Order is Accepted and has no Shipment yet.</summary>
    Received = 1,

    /// <summary>The Shipment is Planned, Released or Booked: everything before collection, so the Shipper can't tell who carries the goods.</summary>
    Scheduled,

    Collected,

    InTransit,

    Delivered,
}

using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ShipperTracking;

public static class TrackingStatuses
{
    /// <summary>
    /// The Tracking status of a Transport Order whose Shipment has the given status, or none yet. A Shipment that goes back to
    /// Planned after a rejection (P4) or a cancellation (P7) stays Scheduled, so the status never moves backwards.
    /// </summary>
    public static TrackingStatus Of(ShipmentStatus? shipment) => shipment switch
    {
        null => TrackingStatus.Received,
        ShipmentStatus.Planned or ShipmentStatus.Released or ShipmentStatus.Booked or ShipmentStatus.Dispatched => TrackingStatus.Scheduled,
        ShipmentStatus.Collected => TrackingStatus.Collected,
        ShipmentStatus.InTransit => TrackingStatus.InTransit,
        ShipmentStatus.Delivered => TrackingStatus.Delivered,
        _ => throw new ArgumentOutOfRangeException(nameof(shipment), shipment, "The TMS doesn't know which Tracking status this Shipment status shows the Shipper."),
    };
}

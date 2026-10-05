using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>A Tracking event moved a Shipment to Delivered (P5).</summary>
public sealed class ShipmentDeliveredEvent(Shipment shipment, TrackingEvent trackingEvent) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public TrackingEvent TrackingEvent { get; } = trackingEvent;
}

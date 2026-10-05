using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>A Tracking event moved a Shipment to In transit (P5).</summary>
public sealed class ShipmentInTransitEvent(Shipment shipment, TrackingEvent trackingEvent) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public TrackingEvent TrackingEvent { get; } = trackingEvent;
}

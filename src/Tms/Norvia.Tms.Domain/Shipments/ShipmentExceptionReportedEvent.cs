using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>An exception Tracking event (Collection failed, Delivery failed or Damaged) was recorded for a Shipment (P5). The status is unchanged.</summary>
public sealed class ShipmentExceptionReportedEvent(Shipment shipment, TrackingEvent trackingEvent) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public TrackingEvent TrackingEvent { get; } = trackingEvent;
}

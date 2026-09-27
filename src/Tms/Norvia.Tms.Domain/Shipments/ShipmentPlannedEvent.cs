using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Shipments;

public sealed class ShipmentPlannedEvent(Shipment shipment) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;
}

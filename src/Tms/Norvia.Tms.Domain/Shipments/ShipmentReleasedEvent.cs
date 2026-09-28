using Ardalis.SharedKernel;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <param name="transportOrder">The Transport Order the Shipment carries out, whose details the release hands over.</param>
public sealed class ShipmentReleasedEvent(Shipment shipment, TransportOrder transportOrder, ShipmentRelease release) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public TransportOrder TransportOrder { get; } = transportOrder;

    public ShipmentRelease Release { get; } = release;
}

using Ardalis.SharedKernel;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <param name="transportOrder">The Transport Order the Shipment carries out, whose details the release hands over.</param>
/// <param name="carrierCost">The Carrier cost the release froze on the Carrier booking (P9); null for an Own fleet Shipment.</param>
public sealed class ShipmentReleasedEvent(Shipment shipment, TransportOrder transportOrder, ShipmentRelease release, CarrierCost? carrierCost = null) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public TransportOrder TransportOrder { get; } = transportOrder;

    public ShipmentRelease Release { get; } = release;

    public CarrierCost? CarrierCost { get; } = carrierCost;
}

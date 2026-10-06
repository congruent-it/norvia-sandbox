using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Planner agreed a delivery window for a Collected or In transit Shipment (P7).</summary>
public sealed class ShipmentDeliveryWindowAgreedEvent(Shipment shipment, AgreedDeliveryWindow agreed) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public AgreedDeliveryWindow Agreed { get; } = agreed;
}

using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.TransportOrders;

public sealed class TransportOrderRejectedEvent(TransportOrder transportOrder) : DomainEventBase
{
    public TransportOrder TransportOrder { get; } = transportOrder;
}

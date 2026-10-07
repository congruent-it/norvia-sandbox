using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.TransportOrders;

public sealed class TransportOrderBillableEvent(TransportOrder transportOrder) : DomainEventBase
{
    public TransportOrder TransportOrder { get; } = transportOrder;
}

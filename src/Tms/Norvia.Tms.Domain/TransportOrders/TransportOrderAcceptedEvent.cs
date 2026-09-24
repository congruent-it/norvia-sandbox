using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.TransportOrders;

public sealed class TransportOrderAcceptedEvent(TransportOrder transportOrder) : DomainEventBase
{
    public TransportOrder TransportOrder { get; } = transportOrder;
}

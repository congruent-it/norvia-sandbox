using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.TransportOrders;

/// <param name="correlationId">
/// The Message trail the rating belongs to: the intake's for the first rating, a re-rate's own for a re-rate.
/// </param>
public sealed class TransportOrderRatedEvent(TransportOrder transportOrder, CorrelationId correlationId) : DomainEventBase
{
    public TransportOrder TransportOrder { get; } = transportOrder;

    public CorrelationId CorrelationId { get; } = correlationId;
}

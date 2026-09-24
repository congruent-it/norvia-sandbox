using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// A Shipper's instruction for Norvia to move goods from an origin to a destination. The TMS owns it (ADR 0002).
/// </summary>
public sealed class TransportOrder : EntityBase<TransportOrderId>, IAggregateRoot
{
    private TransportOrder(
        TransportOrderId id,
        ShipperId shipperId,
        TransportOrderDetails details,
        TransportOrderNumber number,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        Id = id;
        ShipperId = shipperId;
        Details = details;
        Number = number;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
        Status = TransportOrderStatus.Accepted;
    }

    public TransportOrderNumber Number { get; private set; }

    public ShipperId ShipperId { get; private set; }

    public TransportOrderStatus Status { get; private set; }

    public TransportOrderDetails Details { get; private set; }

    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public static TransportOrder Accept(
        ShipperId shipperId,
        TransportOrderDetails details,
        TransportOrderNumber number,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        var order = new TransportOrder(TransportOrderId.New(), shipperId, details, number, correlationId, receivedAt);
        order.RegisterDomainEvent(new TransportOrderAcceptedEvent(order));
        return order;
    }
}

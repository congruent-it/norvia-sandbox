using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// A Shipper's instruction for Norvia to move goods from an origin to a destination. The TMS owns it (ADR 0002).
/// A booking that breaks the Transport Order rules is kept too, as Rejected and without a number.
/// </summary>
public sealed class TransportOrder : EntityBase<TransportOrderId>, IAggregateRoot
{
    private TransportOrder(
        TransportOrderId id,
        ShipperId shipperId,
        TransportOrderDetails details,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        Id = id;
        ShipperId = shipperId;
        Details = details;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
    }

    /// <summary>Null for a Rejected booking: the TMS assigns numbers only on acceptance.</summary>
    public TransportOrderNumber? Number { get; private set; }

    public ShipperId ShipperId { get; private set; }

    public TransportOrderStatus Status { get; private set; }

    public TransportOrderDetails Details { get; private set; }

    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Every rule a Rejected booking broke; empty for an Accepted Transport Order.</summary>
    public IReadOnlyList<RejectionReason> RejectionReasons { get; private set; } = [];

    public static TransportOrder Accept(
        ShipperId shipperId,
        TransportOrderDetails details,
        TransportOrderNumber number,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        var order = new TransportOrder(TransportOrderId.New(), shipperId, details, correlationId, receivedAt)
        {
            Number = number,
            Status = TransportOrderStatus.Accepted,
        };
        order.RegisterDomainEvent(new TransportOrderAcceptedEvent(order));
        return order;
    }

    public static TransportOrder Reject(
        ShipperId shipperId,
        TransportOrderDetails details,
        IReadOnlyList<RejectionReason> reasons,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Count == 0)
        {
            throw new ArgumentException("A booking is rejected for at least one reason.", nameof(reasons));
        }

        var order = new TransportOrder(TransportOrderId.New(), shipperId, details, correlationId, receivedAt)
        {
            Status = TransportOrderStatus.Rejected,
            RejectionReasons = [.. reasons],
        };
        order.RegisterDomainEvent(new TransportOrderRejectedEvent(order));
        return order;
    }
}

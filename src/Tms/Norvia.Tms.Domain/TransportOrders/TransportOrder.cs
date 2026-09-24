using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// A Shipper's instruction for Norvia to move goods from an origin to a destination. The TMS owns it (ADR 0002).
/// A booking that breaks the Transport Order rules is kept too, as Rejected and without a number.
/// A Shipper reference is unique per Shipper: an Accepted Transport Order holds its reference, so a later booking with
/// the same reference is either a repeat of it or a reuse, which the rules reject.
/// </summary>
public sealed class TransportOrder : EntityBase<TransportOrderId>, IAggregateRoot
{
    private TransportOrder(
        TransportOrderId id,
        ShipperId shipperId,
        TransportOrderDetails details,
        IntakeChannel channel,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        Id = id;
        ShipperId = shipperId;
        Details = details;
        Channel = channel;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
    }

    /// <summary>Null for a Rejected booking: the TMS assigns numbers only on acceptance.</summary>
    public TransportOrderNumber? Number { get; private set; }

    public ShipperId ShipperId { get; private set; }

    /// <summary>
    /// The Shipper reference this Transport Order holds for its Shipper. Null for a Rejected booking, which holds no
    /// reference, so the Shipper can book again under it once the booking is corrected.
    /// </summary>
    public string? ShipperReference { get; private set; }

    public TransportOrderStatus Status { get; private set; }

    public TransportOrderDetails Details { get; private set; }

    public IntakeChannel Channel { get; private set; }

    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Every rule a Rejected booking broke; empty for an Accepted Transport Order.</summary>
    public IReadOnlyList<RejectionReason> RejectionReasons { get; private set; } = [];

    /// <summary>Whether a booking under this Transport Order's Shipper reference is the same booking sent again.</summary>
    public bool IsRepeatOf(TransportOrderDetails details) => Details.Equals(details);

    public static TransportOrder Accept(
        ShipperId shipperId,
        TransportOrderDetails details,
        TransportOrderNumber number,
        IntakeChannel channel,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        var order = new TransportOrder(TransportOrderId.New(), shipperId, details, channel, correlationId, receivedAt)
        {
            Number = number,
            Status = TransportOrderStatus.Accepted,
            ShipperReference = details.ShipperReference,
        };
        order.RegisterDomainEvent(new TransportOrderAcceptedEvent(order));
        return order;
    }

    public static TransportOrder Reject(
        ShipperId shipperId,
        TransportOrderDetails details,
        IReadOnlyList<RejectionReason> reasons,
        IntakeChannel channel,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Count == 0)
        {
            throw new ArgumentException("A booking is rejected for at least one reason.", nameof(reasons));
        }

        var order = new TransportOrder(TransportOrderId.New(), shipperId, details, channel, correlationId, receivedAt)
        {
            Status = TransportOrderStatus.Rejected,
            RejectionReasons = [.. reasons],
        };
        order.RegisterDomainEvent(new TransportOrderRejectedEvent(order));
        return order;
    }
}

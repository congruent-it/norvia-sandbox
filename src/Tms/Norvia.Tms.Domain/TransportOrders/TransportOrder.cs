using Ardalis.SharedKernel;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;
using static System.FormattableString;

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

    /// <summary>Null for a Rejected booking, which is never rated.</summary>
    public RatingResult? RatingResult { get; private set; }

    /// <summary>The Freight Charge frozen on the Transport Order once it is Rated; null until then.</summary>
    public FreightCharge? FreightCharge { get; private set; }

    /// <summary>When the Transport Order was Rated; null until then.</summary>
    public DateTimeOffset? RatedAt { get; private set; }

    /// <summary>Why the Rate Card couldn't price the Transport Order, once Rating failed; null otherwise.</summary>
    public RatingFailure? RatingFailure { get; private set; }

    /// <summary>When Rating failed; null unless it did.</summary>
    public DateTimeOffset? RatingFailedAt { get; private set; }

    /// <summary>The key of the last re-rate, so that re-rate sent again is known as a repeat; null until one runs.</summary>
    public IdempotencyKey? LastReRateKey { get; private set; }

    /// <summary>Whether a booking under this Transport Order's Shipper reference is the same booking sent again.</summary>
    public bool IsRepeatOf(TransportOrderDetails details) => Details.Equals(details);

    /// <summary>
    /// Rates a Transport Order that is Not yet rated, from its Shipper's Rate Card: it is Rated with the Freight Charge
    /// frozen on it, or, when there is no Rate Card or it can't price the order, Rating failed with the reason. Either
    /// way the order stays Accepted. Returns false, changing nothing, when it isn't waiting for Rating.
    /// </summary>
    public bool Rate(RateCard? rateCard, DateTimeOffset ratedAt)
    {
        if (RatingResult != TransportOrders.RatingResult.NotYetRated)
        {
            return false;
        }

        ApplyRating(rateCard, ratedAt);
        return true;
    }

    /// <summary>
    /// Rates a Transport Order whose Rating failed again, the same way as the first time: the Rate Card version valid on
    /// the collection date, not today's. It is Rated, or Rating failed with the new reason, and raises its outcome event
    /// again. Anything but Rating failed is refused, changing nothing: a frozen Freight Charge stays frozen, and a wrong
    /// one is corrected with a Credit note in the ERP. The last re-rate sent again under its key is a repeat that changes
    /// nothing, so a retry gets the answer the re-rate got rather than a refusal.
    /// </summary>
    public ReRateOutcome ReRate(RateCard? rateCard, DateTimeOffset ratedAt, IdempotencyKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key == LastReRateKey)
        {
            return ReRateOutcome.Repeat;
        }

        if (RatingResult != TransportOrders.RatingResult.RatingFailed)
        {
            return ReRateOutcome.Refused;
        }

        ApplyRating(rateCard, ratedAt);
        LastReRateKey = key;
        return ReRateOutcome.ReRated;
    }

    private void ApplyRating(RateCard? rateCard, DateTimeOffset ratedAt)
    {
        var outcome = rateCard?.Rate(Details)
            ?? RatingOutcome.Failed(RatingFailureCode.NoRateCard, Invariant($"Shipper {ShipperId} has no Rate Card."));
        if (outcome.FreightCharge is { } freightCharge)
        {
            RatingResult = TransportOrders.RatingResult.Rated;
            FreightCharge = freightCharge;
            RatedAt = ratedAt;
            RatingFailure = null;
            RatingFailedAt = null;
            RegisterDomainEvent(new TransportOrderRatedEvent(this));
        }
        else
        {
            RatingResult = TransportOrders.RatingResult.RatingFailed;
            RatingFailure = outcome.Failure;
            RatingFailedAt = ratedAt;
            RegisterDomainEvent(new TransportOrderRatingFailedEvent(this));
        }
    }

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
            RatingResult = channel == IntakeChannel.Wms ? TransportOrders.RatingResult.NotRatedSeparately : TransportOrders.RatingResult.NotYetRated,
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

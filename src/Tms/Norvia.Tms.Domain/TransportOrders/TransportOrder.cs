using Ardalis.SharedKernel;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
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

    /// <summary>
    /// <see cref="Number"/> as text. The database computes it from the number's column, so a search can match part of the number
    /// in SQL, which it can't through the number's value conversion.
    /// </summary>
    public string? NumberText { get; private set; }

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

    /// <summary>Where the Transport Order stands for billing (P9); null for a Rejected booking, which is never billed.</summary>
    public BillingState? BillingState { get; private set; }

    /// <summary>When the Shipment was Delivered, frozen when the Transport Order became Billable; null until then.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>When the Transport Order became Billable; null until then.</summary>
    public DateTimeOffset? BecameBillableAt { get; private set; }

    /// <summary>The Message trail of the transaction that made the Transport Order Billable; null until then.</summary>
    public CorrelationId? BillingCorrelationId { get; private set; }

    /// <summary>Whether Customer service released the Transport Order without a Proof of delivery (P9); false until it is.</summary>
    public bool ReleasedWithoutProofOfDelivery { get; private set; }

    /// <summary>Who in Customer service released the Transport Order without a Proof of delivery; null until they do.</summary>
    public string? ReleasedBy { get; private set; }

    /// <summary>When Customer service released the Transport Order without a Proof of delivery; null until then.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>The remark Customer service gave when releasing the Transport Order without a Proof of delivery; null until then.</summary>
    public string? ReleaseRemark { get; private set; }

    /// <summary>The longest remark of a release without a Proof of delivery.</summary>
    public const int MaxReleaseRemarkLength = 2000;

    /// <summary>The longest name of who released.</summary>
    public const int MaxReleasedByLength = 200;

    /// <summary>The number of the Transport Order's one Shipment, once it is Planned; null until then.</summary>
    public ShipmentNumber? ShipmentNumber { get; private set; }

    /// <summary><see cref="ShipmentNumber"/> as text, computed by the database like <see cref="NumberText"/>.</summary>
    public string? ShipmentNumberText { get; private set; }

    /// <summary>The key of the assignment that planned the Transport Order, so that assignment sent again is known as a repeat.</summary>
    public IdempotencyKey? PlanningKey { get; private set; }

    /// <summary>Whether the Transport Order was planned by the assignment sent under this key.</summary>
    public bool WasPlannedUnder(IdempotencyKey key) => PlanningKey is not null && PlanningKey == key;

    /// <summary>
    /// Plans an Accepted Transport Order: it gets its one Shipment (ADR 0007), with the Shipment number and the status
    /// Planned, carried by Own fleet or a Carrier, and becomes Planned, its last status. The Shipment is on the
    /// assignment's own Message trail; the Transport Order keeps its intake's. Its rating result never blocks planning.
    /// Returns null, changing nothing, for a Transport Order that isn't Accepted, as it has its Shipment already or never
    /// gets one.
    /// </summary>
    public Shipment? Plan(ShipmentNumber shipmentNumber, CarriedBy carriedBy, IdempotencyKey key, CorrelationId correlationId, DateTimeOffset plannedAt)
    {
        ArgumentNullException.ThrowIfNull(shipmentNumber);
        ArgumentNullException.ThrowIfNull(carriedBy);
        ArgumentNullException.ThrowIfNull(key);
        if (Status != TransportOrderStatus.Accepted)
        {
            return null;
        }

        Status = TransportOrderStatus.Planned;
        ShipmentNumber = shipmentNumber;
        ShipmentNumberText = shipmentNumber.Value;
        PlanningKey = key;
        return Shipment.Plan(shipmentNumber, this, carriedBy, correlationId, plannedAt);
    }

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

    /// <summary>
    /// Whether the order's own conditions to be Billable hold (P9): it is a Transport-only order that isn't Billable yet, it is
    /// Rated and it has its Shipment. Only then do the Shipment's facts decide.
    /// </summary>
    public bool AwaitsBillingFacts =>
        BillingState == TransportOrders.BillingState.NotYetBillable
        && RatingResult == TransportOrders.RatingResult.Rated
        && ShipmentNumber is not null;

    /// <summary>
    /// Makes a Transport-only order Billable when all its conditions hold (P9, "no POD, no invoice"): its Shipment is
    /// Delivered, it is Rated, its Shipment has a current Proof of delivery and no open Exception. It then raises TransportOrderBillable once.
    /// Returns false, changing nothing, when a condition doesn't hold yet, when the order is billed through the Fulfilment
    /// Contract, and when it is Billable already: Billable is final.
    /// </summary>
    /// <param name="facts">What is known about the Shipment.</param>
    /// <param name="correlationId">The Message trail of the transaction that may make the order Billable.</param>
    public bool TryBecomeBillable(BillingFacts facts, CorrelationId correlationId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (!AwaitsBillingFacts
            || facts is not { DeliveredAt: { } deliveredAt, HasOpenException: false }
            || !(facts.HasCurrentProofOfDelivery || ReleasedWithoutProofOfDelivery))
        {
            return false;
        }

        BillingState = TransportOrders.BillingState.Billable;
        DeliveredAt = deliveredAt;
        BecameBillableAt = now;
        BillingCorrelationId = correlationId;
        RegisterDomainEvent(new TransportOrderBillableEvent(this));
        return true;
    }

    /// <summary>
    /// Customer service releases a Delivered, Not yet billable order without a Proof of delivery (P9), so a Carrier that never
    /// sends one can't hold up billing for good. It lifts only <c>NoProofOfDelivery</c>: the order becomes Billable only through
    /// <see cref="TryBecomeBillable"/>, and publishes nothing of its own. Returns the refusal, changing nothing, when the order is
    /// Billable, billed through the Fulfilment Contract, released already or not Delivered.
    /// </summary>
    /// <param name="deliveredAt">When the order's Shipment was Delivered; null while it isn't.</param>
    public BillingRefusal? ReleaseWithoutProofOfDelivery(DateTimeOffset? deliveredAt, string remark, string releasedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remark);
        ArgumentException.ThrowIfNullOrWhiteSpace(releasedBy);
        if (ReleaseRefusal(deliveredAt) is { } refusal)
        {
            return refusal;
        }

        ReleasedWithoutProofOfDelivery = true;
        ReleasedBy = releasedBy;
        ReleasedAt = now;
        ReleaseRemark = remark;
        return null;
    }

    /// <summary>
    /// The rule of P9 for releasing without a Proof of delivery, on its own: why Customer service can't release this order, or null
    /// when it can. <see cref="ReleaseWithoutProofOfDelivery"/> applies it, and so does whatever tells Customer service, or the
    /// Customer service agent, whether an order may be released, so there is one copy of the rule.
    /// </summary>
    /// <param name="deliveredAt">When the order's Shipment was Delivered; null while it isn't.</param>
    public BillingRefusal? ReleaseRefusal(DateTimeOffset? deliveredAt)
    {
        switch (BillingState)
        {
            case TransportOrders.BillingState.Billable:
                return new(BillingRefusalCode.AlreadyBillable, Invariant($"Transport Order {Number} is Billable already."));
            case TransportOrders.BillingState.NotBilledSeparately:
                return new(BillingRefusalCode.NotBilledSeparately, Invariant($"Transport Order {Number} is billed through the Fulfilment Contract."));
            case null:
                throw new InvalidOperationException("A Rejected booking has no billing state to release.");
        }

        if (deliveredAt is null)
        {
            return new(BillingRefusalCode.ShipmentNotDelivered, Invariant($"The Shipment of Transport Order {Number} isn't Delivered."));
        }

        return ReleasedWithoutProofOfDelivery
            ? new(BillingRefusalCode.AlreadyReleased, Invariant($"Transport Order {Number} was released without a Proof of delivery already."))
            : null;
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
            NumberText = number.Value,
            Status = TransportOrderStatus.Accepted,
            ShipperReference = details.ShipperReference,
            BillingState = channel == IntakeChannel.Wms ? TransportOrders.BillingState.NotBilledSeparately : TransportOrders.BillingState.NotYetBillable,
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

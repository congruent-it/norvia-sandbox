using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// A request to one Carrier to carry one Shipment, together with that Carrier's answer. A Shipment can have several
/// Carrier bookings over its life, one after each that ends without being carried out, but only one open at a time
/// (P4). The TMS owns it. Sent/not-delivered reporting and withdrawal are P4's; cancelling a Confirmed one is P7's.
/// </summary>
public sealed class CarrierBooking : EntityBase<CarrierBookingId>, IAggregateRoot
{
    private CarrierBooking(
        CarrierBookingId id,
        CarrierBookingNumber number,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        CarrierId carrierId,
        CorrelationId correlationId,
        DateTimeOffset requestedAt,
        CarrierCost? carrierCost)
    {
        Id = id;
        Number = number;
        ShipmentId = shipmentId;
        ShipmentNumber = shipmentNumber;
        CarrierId = carrierId;
        CorrelationId = correlationId;
        RequestedAt = requestedAt;
        CarrierCost = carrierCost;
        Status = CarrierBookingStatus.Requested;
    }

    /// <summary>The longest cancellation reason the TMS keeps.</summary>
    public const int MaxCancellationReasonLength = 500;

    public CarrierBookingNumber Number { get; private set; }

    public ShipmentId ShipmentId { get; private set; }

    public ShipmentNumber ShipmentNumber { get; private set; }

    public CarrierId CarrierId { get; private set; }

    public CarrierBookingStatus Status { get; private set; }

    /// <summary>The Message trail of the release that created the booking.</summary>
    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>
    /// What Norvia pays the Carrier for the booking, calculated at release and frozen: never recalculated (P9). A new
    /// release after a rejection makes a new booking with a new Carrier cost. Null only for a booking requested before
    /// release priced it.
    /// </summary>
    public CarrierCost? CarrierCost { get; private set; }

    /// <summary>The Message trail of the Carrier's own answer; null while the booking is Requested.</summary>
    public CorrelationId? AnswerCorrelationId { get; private set; }

    public DateTimeOffset? AnsweredAt { get; private set; }

    /// <summary>The Carrier's own number for the booking, given on confirmation; null otherwise.</summary>
    public string? CarrierBookingReference { get; private set; }

    /// <summary>The time the Carrier plans to collect, given on confirmation; null otherwise.</summary>
    public DateTimeOffset? PlannedCollectionTime { get; private set; }

    public CarrierBookingRejectionReason? RejectionReason { get; private set; }

    public string? RejectionReasonText { get; private set; }

    /// <summary>When the Carrier acknowledged receipt, reported by the P4 booking Logic App; null until then.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>The technical cause's HTTP status code from the Carrier's endpoint; null unless <see cref="Status"/> is NotDelivered.</summary>
    public int? NotDeliveredStatusCode { get; private set; }

    /// <summary>The technical cause's message; null unless <see cref="Status"/> is NotDelivered.</summary>
    public string? NotDeliveredMessage { get; private set; }

    public DateTimeOffset? NotDeliveredAt { get; private set; }

    /// <summary>The Planner's idempotency key for the withdrawal that ended it; null unless it was withdrawn.</summary>
    public IdempotencyKey? WithdrawalKey { get; private set; }

    /// <summary>The withdrawal's own Message trail; null unless it was withdrawn.</summary>
    public CorrelationId? WithdrawalCorrelationId { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>Why the Planner cancelled the Confirmed booking (P7); null unless it was cancelled.</summary>
    public string? CancellationReasonText { get; private set; }

    /// <summary>The Exception the Planner cancelled it from; null unless it was cancelled.</summary>
    public ExceptionNumber? CancellationExceptionNumber { get; private set; }

    /// <summary>The Planner's idempotency key for the cancellation that ended it; null unless it was cancelled.</summary>
    public IdempotencyKey? CancellationKey { get; private set; }

    /// <summary>The cancellation's own Message trail; null unless it was cancelled.</summary>
    public CorrelationId? CancellationCorrelationId { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>When the Carrier acknowledged the cancellation or withdrawal message, reported by the Carrier cancellation Logic App (P7); null until then.</summary>
    public DateTimeOffset? CarrierToldAt { get; private set; }

    /// <summary>The technical cause's HTTP status code when the cancellation or withdrawal message couldn't be delivered; null unless flagged Carrier not told.</summary>
    public int? CarrierNotToldStatusCode { get; private set; }

    /// <summary>The technical cause's message; null unless flagged Carrier not told.</summary>
    public string? CarrierNotToldMessage { get; private set; }

    public DateTimeOffset? CarrierNotToldAt { get; private set; }

    /// <summary>True when the booking is Withdrawn or Cancelled and the Carrier cancellation Logic App reported it as not delivered.</summary>
    public bool IsCarrierNotTold => CarrierNotToldAt is not null;

    /// <summary>
    /// Requests a Carrier booking for a Shipment's release, Requested, with the Carrier cost the release froze on it
    /// (P9). Raises no domain event of its own: the release that creates it announces both in <c>ShipmentReleased</c>.
    /// </summary>
    public static CarrierBooking Request(
        CarrierBookingNumber number,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        CarrierId carrierId,
        CorrelationId correlationId,
        DateTimeOffset requestedAt,
        CarrierCost? carrierCost = null)
    {
        ArgumentNullException.ThrowIfNull(carrierId);
        return new CarrierBooking(CarrierBookingId.New(), number, shipmentId, shipmentNumber, carrierId, correlationId, requestedAt, carrierCost);
    }

    /// <summary>
    /// Records the Carrier's confirmation of a Requested booking with its own booking reference and, optionally, a
    /// planned collection time: the booking becomes Confirmed and raises <c>CarrierBookingConfirmed</c>. Returns false,
    /// changing nothing, for a booking that isn't Requested.
    /// </summary>
    /// <param name="transportOrderNumber">The Transport Order the booking's Shipment carries out, which the event carries.</param>
    public bool Confirm(
        string carrierBookingReference,
        DateTimeOffset? plannedCollectionTime,
        TransportOrderNumber transportOrderNumber,
        CorrelationId answerCorrelationId,
        DateTimeOffset answeredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierBookingReference);
        ArgumentNullException.ThrowIfNull(transportOrderNumber);
        ArgumentNullException.ThrowIfNull(answerCorrelationId);
        if (Status != CarrierBookingStatus.Requested)
        {
            return false;
        }

        CarrierBookingReference = carrierBookingReference;
        PlannedCollectionTime = plannedCollectionTime;
        AnswerCorrelationId = answerCorrelationId;
        AnsweredAt = answeredAt;
        Status = CarrierBookingStatus.Confirmed;
        RegisterDomainEvent(new CarrierBookingConfirmedEvent(this, transportOrderNumber));
        return true;
    }

    /// <summary>
    /// Records the Carrier's rejection of a Requested booking with a reason: the booking becomes Rejected and raises
    /// <c>CarrierBookingRejected</c>. Returns false, changing nothing, for a booking that isn't Requested.
    /// </summary>
    public bool Reject(CarrierBookingRejectionReason reason, string? reasonText, CorrelationId answerCorrelationId, DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(answerCorrelationId);
        if (Status != CarrierBookingStatus.Requested)
        {
            return false;
        }

        RejectionReason = reason;
        RejectionReasonText = reasonText;
        AnswerCorrelationId = answerCorrelationId;
        AnsweredAt = answeredAt;
        Status = CarrierBookingStatus.Rejected;
        RegisterDomainEvent(new CarrierBookingRejectedEvent(this));
        return true;
    }

    /// <summary>True when a confirmation exactly repeats the one already recorded: the same reference and planned collection time.</summary>
    public bool IsRepeatOfConfirm(string carrierBookingReference, DateTimeOffset? plannedCollectionTime) =>
        Status == CarrierBookingStatus.Confirmed
        && CarrierBookingReference == carrierBookingReference
        && PlannedCollectionTime == plannedCollectionTime;

    /// <summary>True when a rejection exactly repeats the one already recorded: the same reason code and text.</summary>
    public bool IsRepeatOfReject(CarrierBookingRejectionReason reason, string? reasonText) =>
        Status == CarrierBookingStatus.Rejected && RejectionReason == reason && RejectionReasonText == reasonText;

    /// <summary>
    /// Records that the P4 booking Logic App's request reached the Carrier, on a Requested booking. Raises no domain
    /// event: a sent report publishes nothing. Returns false, changing nothing, for a booking that isn't Requested (for
    /// example, withdrawn while the report was in flight).
    /// </summary>
    public bool MarkSent(DateTimeOffset sentAt)
    {
        if (Status != CarrierBookingStatus.Requested)
        {
            return false;
        }

        SentAt = sentAt;
        return true;
    }

    /// <summary>
    /// Records that the P4 booking Logic App's request never reached the Carrier, with its technical cause: the booking
    /// becomes NotDelivered and raises <c>CarrierBookingNotDelivered</c>. Returns false, changing nothing, for a booking
    /// that isn't Requested (for example, withdrawn while the report was in flight).
    /// </summary>
    public bool ReportNotDelivered(int statusCode, string message, CorrelationId correlationId, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (Status != CarrierBookingStatus.Requested)
        {
            return false;
        }

        NotDeliveredStatusCode = statusCode;
        NotDeliveredMessage = message;
        NotDeliveredAt = occurredAt;
        Status = CarrierBookingStatus.NotDelivered;
        RegisterDomainEvent(new CarrierBookingNotDeliveredEvent(this, correlationId));
        return true;
    }

    /// <summary>
    /// The Planner takes a Requested booking back: it becomes Withdrawn and raises <c>CarrierBookingWithdrawn</c>.
    /// Returns false, changing nothing, for a booking that isn't Requested.
    /// </summary>
    public bool Withdraw(IdempotencyKey key, CorrelationId correlationId, DateTimeOffset withdrawnAt)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (Status != CarrierBookingStatus.Requested)
        {
            return false;
        }

        WithdrawalKey = key;
        WithdrawalCorrelationId = correlationId;
        WithdrawnAt = withdrawnAt;
        Status = CarrierBookingStatus.Withdrawn;
        RegisterDomainEvent(new CarrierBookingWithdrawnEvent(this));
        return true;
    }

    /// <summary>
    /// Norvia ends a Confirmed booking before collection, from an Exception, with a reason (P7): it becomes Cancelled and raises
    /// <c>CarrierBookingCancelled</c>. Returns false, changing nothing, for a booking that isn't Confirmed. The reason is checked
    /// at the boundary first.
    /// </summary>
    public bool Cancel(string reasonText, ExceptionNumber exceptionNumber, IdempotencyKey key, CorrelationId correlationId, DateTimeOffset cancelledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonText);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reasonText.Length, MaxCancellationReasonLength);
        ArgumentNullException.ThrowIfNull(exceptionNumber);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (Status != CarrierBookingStatus.Confirmed)
        {
            return false;
        }

        CancellationReasonText = reasonText;
        CancellationExceptionNumber = exceptionNumber;
        CancellationKey = key;
        CancellationCorrelationId = correlationId;
        CancelledAt = cancelledAt;
        Status = CarrierBookingStatus.Cancelled;
        RegisterDomainEvent(new CarrierBookingCancelledEvent(this));
        return true;
    }

    /// <summary>
    /// Records that the Carrier cancellation Logic App's message reached the Carrier, on a Withdrawn or Cancelled booking not yet
    /// reported (P7). Raises no domain event. Returns false, changing nothing, for any other booking, or one already reported told or not told.
    /// </summary>
    public bool MarkCarrierTold(DateTimeOffset toldAt)
    {
        if (!AwaitsCarrierReport)
        {
            return false;
        }

        CarrierToldAt = toldAt;
        return true;
    }

    /// <summary>
    /// Records that the Carrier cancellation Logic App's message never reached the Carrier, with its technical cause, on a Withdrawn
    /// or Cancelled booking not yet reported (P7): the booking is flagged Carrier not told and raises <c>CarrierCancellationNotDelivered</c>.
    /// Returns false, changing nothing, for any other booking, or one already reported told or not told.
    /// </summary>
    public bool ReportCarrierNotTold(int statusCode, string message, CorrelationId correlationId, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (!AwaitsCarrierReport)
        {
            return false;
        }

        CarrierNotToldStatusCode = statusCode;
        CarrierNotToldMessage = message;
        CarrierNotToldAt = occurredAt;
        RegisterDomainEvent(new CarrierCancellationNotDeliveredEvent(this, correlationId));
        return true;
    }

    /// <summary>Norvia ended the booking, by withdrawing or cancelling it: the Carrier is told about these, and only these (P7).</summary>
    public bool IsEndedByNorvia => Status is CarrierBookingStatus.Withdrawn or CarrierBookingStatus.Cancelled;

    /// <summary>The Carrier cancellation Logic App's report is still due: the booking is ended by Norvia and not yet reported told or not told.</summary>
    public bool AwaitsCarrierReport => IsEndedByNorvia && CarrierToldAt is null && CarrierNotToldAt is null;

    /// <summary>The cancellation's own trail made under the key; null when none was.</summary>
    public CorrelationId? CancellationUnder(IdempotencyKey key) => CancellationKey == key ? CancellationCorrelationId : null;

    /// <summary>The withdrawal's own trail made under the key; null when none was.</summary>
    public CorrelationId? WithdrawalUnder(IdempotencyKey key) => WithdrawalKey == key ? WithdrawalCorrelationId : null;
}

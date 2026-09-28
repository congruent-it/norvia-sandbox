using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// A request to one Carrier to carry one Shipment, together with that Carrier's answer. A Shipment can have several
/// Carrier bookings over its life, one after each that ends without being carried out, but only one open at a time
/// (P4). The TMS owns it. This build covers only the happy path: an answer for a booking that isn't Requested, field
/// validation, idempotent repeats, sent/not-delivered reporting and withdrawal are later tickets.
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
        DateTimeOffset requestedAt)
    {
        Id = id;
        Number = number;
        ShipmentId = shipmentId;
        ShipmentNumber = shipmentNumber;
        CarrierId = carrierId;
        CorrelationId = correlationId;
        RequestedAt = requestedAt;
        Status = CarrierBookingStatus.Requested;
    }

    public CarrierBookingNumber Number { get; private set; }

    public ShipmentId ShipmentId { get; private set; }

    public ShipmentNumber ShipmentNumber { get; private set; }

    public CarrierId CarrierId { get; private set; }

    public CarrierBookingStatus Status { get; private set; }

    /// <summary>The Message trail of the release that created the booking.</summary>
    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The Message trail of the Carrier's own answer; null while the booking is Requested.</summary>
    public CorrelationId? AnswerCorrelationId { get; private set; }

    public DateTimeOffset? AnsweredAt { get; private set; }

    /// <summary>The Carrier's own number for the booking, given on confirmation; null otherwise.</summary>
    public string? CarrierBookingReference { get; private set; }

    /// <summary>The time the Carrier plans to collect, given on confirmation; null otherwise.</summary>
    public DateTimeOffset? PlannedCollectionTime { get; private set; }

    public CarrierBookingRejectionReason? RejectionReason { get; private set; }

    public string? RejectionReasonText { get; private set; }

    /// <summary>
    /// Requests a Carrier booking for a Shipment's release, Requested. Raises no domain event of its own: the release
    /// that creates it announces both in <c>ShipmentReleased</c>.
    /// </summary>
    public static CarrierBooking Request(
        CarrierBookingNumber number,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        CarrierId carrierId,
        CorrelationId correlationId,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(carrierId);
        return new CarrierBooking(CarrierBookingId.New(), number, shipmentId, shipmentNumber, carrierId, correlationId, requestedAt);
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
}

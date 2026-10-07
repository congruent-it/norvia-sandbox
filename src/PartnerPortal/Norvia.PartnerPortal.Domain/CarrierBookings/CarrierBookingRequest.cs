using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// A request Norvia sent a Carrier Persona to carry a Shipment, in the Carrier's inbox. The Carrier booking number is
/// its key: the same request received again is the one the inbox already has.
/// </summary>
public sealed class CarrierBookingRequest : EntityBase<CarrierBookingRequestId>, IAggregateRoot
{
    private CarrierBookingRequest(
        CarrierBookingRequestId id,
        PersonaId personaId,
        string carrierBookingNumber,
        string shipmentNumber,
        string correlationId,
        DateTimeOffset receivedAt,
        CarrierBookingDetails details)
    {
        Id = id;
        PersonaId = personaId;
        CarrierBookingNumber = carrierBookingNumber;
        ShipmentNumber = shipmentNumber;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
        Details = details;
    }

    public PersonaId PersonaId { get; private set; }

    public string CarrierBookingNumber { get; private set; }

    public string ShipmentNumber { get; private set; }

    /// <summary>The release's correlation ID, which the request arrived with.</summary>
    public string CorrelationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public CarrierBookingDetails Details { get; private set; }

    public CarrierBookingRequestStatus Status { get; private set; } = CarrierBookingRequestStatus.Open;

    /// <summary>When the request left Open: answered, or found withdrawn by Norvia.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The correlation ID Norvia gave the answer that closed the request; null while Open, or when Norvia never replied.</summary>
    public string? AnswerCorrelationId { get; private set; }

    /// <summary>The Carrier's own booking reference, once it confirmed.</summary>
    public string? CarrierBookingReference { get; private set; }

    public DateTimeOffset? PlannedCollection { get; private set; }

    /// <summary>Why Norvia cancelled the confirmed booking; null unless it did.</summary>
    public string? CancellationText { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public CarrierRejectionReason? RejectionReason { get; private set; }

    public string? RejectionText { get; private set; }

    /// <summary>The mode the Carrier's policy had when the request arrived, if it was an automatic one. It stays the request's whatever the policy becomes.</summary>
    public CarrierAnswerMode? AutoAnswerMode { get; private set; }

    public CarrierRejectionReason? AutoAnswerReason { get; private set; }

    /// <summary>When the automatic answer is due; null for a request answered by hand, and once the request is closed.</summary>
    public DateTimeOffset? AutoAnswerDueAt { get; private set; }

    /// <summary>The delay between the automatic Tracking events, if the Persona's policy was auto-progress when it confirmed. It stays the request's whatever the policy becomes.</summary>
    public TimeSpan? AutoProgressDelay { get; private set; }

    /// <summary>When the next automatic Tracking event, or once the Shipment is Delivered its Proof of delivery, is due; null for a request sent by hand, and once the Proof of delivery has gone.</summary>
    public DateTimeOffset? AutoProgressDueAt { get; private set; }

    /// <summary>The last progress Tracking event the Persona sent for the Shipment; null until it sends one.</summary>
    public TrackingEventType? LastProgress { get; private set; }

    public ShipmentStage Stage => TrackingRules.StageOf(LastProgress);

    public bool IsOpen => Status == CarrierBookingRequestStatus.Open;

    public static CarrierBookingRequest Receive(
        PersonaId personaId,
        string carrierBookingNumber,
        string shipmentNumber,
        string correlationId,
        DateTimeOffset receivedAt,
        CarrierBookingDetails details,
        ScheduledAnswer? scheduledAnswer = null)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierBookingNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(details);
        var request = new CarrierBookingRequest(CarrierBookingRequestId.New(), personaId, carrierBookingNumber, shipmentNumber, correlationId, receivedAt, details);
        request.SetSchedule(scheduledAnswer);
        return request;
    }

    /// <summary>
    /// Records the Carrier's answer once Norvia has recorded it. Only an Open request takes one: it returns false, changing
    /// nothing, for a request that is already closed.
    /// </summary>
    public bool Answer(CarrierAnswer answer, string? answerCorrelationId, DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (!IsOpen)
        {
            return false;
        }

        switch (answer)
        {
            case CarrierAnswer.Confirmation confirmation:
                Status = CarrierBookingRequestStatus.Confirmed;
                CarrierBookingReference = confirmation.BookingReference;
                PlannedCollection = confirmation.PlannedCollection;
                break;
            case CarrierAnswer.Rejection rejection:
                Status = CarrierBookingRequestStatus.Rejected;
                RejectionReason = rejection.Reason;
                RejectionText = rejection.Text;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "Every answer is a confirmation or a rejection.");
        }

        Close(answerCorrelationId, answeredAt);
        return true;
    }

    /// <summary>
    /// Records a Tracking event sent for the Shipment of a Confirmed request. Progress moves the Shipment on, and puts the
    /// next automatic event a delay after <paramref name="sentAt"/>; an exception changes nothing. Returns false, changing
    /// nothing, when the request isn't Confirmed or the event isn't a valid next one.
    /// </summary>
    public bool ReportTracking(TrackingEventType type, DateTimeOffset? sentAt = null)
    {
        if (Status != CarrierBookingRequestStatus.Confirmed || !TrackingRules.IsOffered(LastProgress, type))
        {
            return false;
        }

        if (TrackingRules.IsProgress(type))
        {
            LastProgress = type;
            if (AutoProgressDelay is { } delay && sentAt is { } at)
            {
                AutoProgressDueAt = at + delay;
            }
        }

        return true;
    }

    /// <summary>
    /// Starts the automatic Tracking events of a Confirmed Shipment, the first a <paramref name="delay"/> after
    /// <paramref name="confirmedAt"/>. A null delay is a Persona that sends by hand, and schedules nothing.
    /// </summary>
    public void ScheduleAutoProgress(TimeSpan? delay, DateTimeOffset confirmedAt)
    {
        if (Status != CarrierBookingRequestStatus.Confirmed || delay is not { } after)
        {
            return;
        }

        AutoProgressDelay = after;
        AutoProgressDueAt = TrackingRules.NextAutomatic(LastProgress) is null ? null : confirmedAt + after;
    }

    /// <summary>The automatic Tracking event that is due at <paramref name="now"/>; null when none was scheduled or it isn't due yet.</summary>
    public TrackingEventType? AutoProgressDue(DateTimeOffset now) =>
        Status == CarrierBookingRequestStatus.Confirmed && AutoProgressDueAt is { } dueAt && dueAt <= now ? TrackingRules.NextAutomatic(LastProgress) : null;

    /// <summary>Whether the Persona's own Proof of delivery is due at <paramref name="now"/>: the Shipment is Delivered, and a delay has passed since.</summary>
    public bool AutoProofOfDeliveryDue(DateTimeOffset now) =>
        Status == CarrierBookingRequestStatus.Confirmed && LastProgress == TrackingEventType.Delivered && AutoProgressDueAt is { } dueAt && dueAt <= now;

    /// <summary>Records that the Persona's automatic Proof of delivery went out, which ends the schedule. Nothing before the Shipment is Delivered.</summary>
    public void ReportAutoProofOfDelivery()
    {
        if (LastProgress == TrackingEventType.Delivered)
        {
            AutoProgressDueAt = null;
        }
    }

    /// <summary>Puts the automatic Tracking event off to <paramref name="until"/>, after an attempt that didn't move the Shipment on.</summary>
    public void DeferAutoProgress(DateTimeOffset until)
    {
        if (AutoProgressDueAt is not null)
        {
            AutoProgressDueAt = until;
        }
    }

    /// <summary>Records that Norvia no longer has the request open, as it said when the Carrier answered late. Only an Open request is withdrawn.</summary>
    public bool WithdrawnByNorvia(string? answerCorrelationId, DateTimeOffset foundAt)
    {
        if (!IsOpen)
        {
            return false;
        }

        Status = CarrierBookingRequestStatus.WithdrawnByNorvia;
        Close(answerCorrelationId, foundAt);
        return true;
    }

    /// <summary>
    /// Records that Norvia cancelled the booking the Carrier had confirmed, and stops the Shipment's automatic Tracking
    /// events. Only a Confirmed request is cancelled: it returns false, changing nothing, for any other, a cancelled one included.
    /// </summary>
    public bool CancelledByNorvia(string reasonText, DateTimeOffset cancelledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonText);
        if (Status != CarrierBookingRequestStatus.Confirmed)
        {
            return false;
        }

        Status = CarrierBookingRequestStatus.CancelledByNorvia;
        CancellationText = reasonText;
        CancelledAt = cancelledAt;
        AutoProgressDueAt = null;
        return true;
    }

    /// <summary>
    /// The automatic answer that is due at <paramref name="now"/>, confirming with a reference made from
    /// <paramref name="referencePrefix"/>; null when the request is closed, wasn't scheduled or isn't due yet.
    /// </summary>
    public CarrierAnswer? AutoAnswerDue(string referencePrefix, DateTimeOffset now)
    {
        if (!IsOpen || AutoAnswerMode is not { } mode || AutoAnswerDueAt is not { } dueAt || dueAt > now)
        {
            return null;
        }

        return mode switch
        {
            CarrierAnswerMode.AutoConfirm => new CarrierAnswer.Confirmation(BookingReferencePattern.Generate(referencePrefix, now, CarrierBookingNumber)),
            CarrierAnswerMode.AutoReject => new CarrierAnswer.Rejection(
                AutoAnswerReason ?? CarrierRejectionReason.NoCapacity,
                AutoAnswerReason == CarrierRejectionReason.Other ? "Rejected automatically by the Persona's answer mode." : null),
            _ => throw new InvalidOperationException($"{mode} schedules no answer."),
        };
    }

    /// <summary>Puts the automatic answer off to <paramref name="until"/>, after an attempt that left the request open.</summary>
    public void DeferAutoAnswer(DateTimeOffset until)
    {
        if (IsOpen && AutoAnswerDueAt is not null)
        {
            AutoAnswerDueAt = until;
        }
    }

    private void SetSchedule(ScheduledAnswer? scheduled)
    {
        AutoAnswerMode = scheduled?.Mode;
        AutoAnswerReason = scheduled?.RejectionReason;
        AutoAnswerDueAt = scheduled?.DueAt;
    }

    private void Close(string? answerCorrelationId, DateTimeOffset at)
    {
        ClosedAt = at;
        AutoAnswerDueAt = null;
        AnswerCorrelationId = answerCorrelationId;
    }
}

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

    public CarrierRejectionReason? RejectionReason { get; private set; }

    public string? RejectionText { get; private set; }

    public bool IsOpen => Status == CarrierBookingRequestStatus.Open;

    public static CarrierBookingRequest Receive(
        PersonaId personaId,
        string carrierBookingNumber,
        string shipmentNumber,
        string correlationId,
        DateTimeOffset receivedAt,
        CarrierBookingDetails details)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierBookingNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(details);
        return new CarrierBookingRequest(CarrierBookingRequestId.New(), personaId, carrierBookingNumber, shipmentNumber, correlationId, receivedAt, details);
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

    private void Close(string? answerCorrelationId, DateTimeOffset at)
    {
        ClosedAt = at;
        AnswerCorrelationId = answerCorrelationId;
    }
}

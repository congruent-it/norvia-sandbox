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
}

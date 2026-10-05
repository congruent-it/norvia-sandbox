namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>How far a Carrier Persona has taken a Shipment, by the progress Tracking events it sent.</summary>
public enum ShipmentStage
{
    Booked,
    Collected,
    InTransit,
    Delivered,
}

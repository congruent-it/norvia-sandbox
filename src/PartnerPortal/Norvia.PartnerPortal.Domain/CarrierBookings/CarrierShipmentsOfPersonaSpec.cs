using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// A Carrier Persona's Shipments: Confirmed bookings, Booked or later, the latest first. A Delivered one stays, as its
/// Proof of delivery may still be sent.
/// </summary>
public sealed class CarrierShipmentsOfPersonaSpec : Specification<CarrierBookingRequest>
{
    public CarrierShipmentsOfPersonaSpec(PersonaId personaId)
    {
        Query.Where(request => request.PersonaId == personaId
                && request.Status == CarrierBookingRequestStatus.Confirmed)
            .OrderByDescending(request => request.ReceivedAt)
            .AsNoTracking();
    }
}

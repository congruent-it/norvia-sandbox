using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// A Carrier Persona's Shipments still to deliver: Confirmed bookings that are Booked, Collected or In transit, the latest
/// first.
/// </summary>
public sealed class CarrierShipmentsOfPersonaSpec : Specification<CarrierBookingRequest>
{
    public CarrierShipmentsOfPersonaSpec(PersonaId personaId)
    {
        Query.Where(request => request.PersonaId == personaId
                && request.Status == CarrierBookingRequestStatus.Confirmed
                && request.LastProgress != TrackingEventType.Delivered)
            .OrderByDescending(request => request.ReceivedAt)
            .AsNoTracking();
    }
}

using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// A Carrier Persona's Confirmed bookings it has reported Delivered, oldest first: what it may invoice (E2). Whether it
/// has invoiced one already is for the Carrier invoices to say.
/// </summary>
public sealed class CarrierDeliveriesOfPersonaSpec : Specification<CarrierBookingRequest>
{
    public CarrierDeliveriesOfPersonaSpec(PersonaId personaId)
    {
        Query.Where(request => request.PersonaId == personaId
                && request.Status == CarrierBookingRequestStatus.Confirmed
                && request.LastProgress == TrackingEventType.Delivered)
            .OrderBy(request => request.ReceivedAt)
            .AsNoTracking();
    }
}

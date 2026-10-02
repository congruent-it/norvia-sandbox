using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The booking requests in a Carrier Persona's inbox, the latest first.</summary>
public sealed class CarrierBookingRequestsOfPersonaSpec : Specification<CarrierBookingRequest>
{
    public CarrierBookingRequestsOfPersonaSpec(PersonaId personaId)
    {
        PersonaId = personaId;
        Query.Where(request => request.PersonaId == personaId)
            .OrderByDescending(request => request.ReceivedAt)
            .AsNoTracking();
    }

    public PersonaId PersonaId { get; }
}

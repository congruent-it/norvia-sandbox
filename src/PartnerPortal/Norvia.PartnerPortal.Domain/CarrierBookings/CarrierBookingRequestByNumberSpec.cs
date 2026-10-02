using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The request a Carrier Persona already has for a Carrier booking number.</summary>
public sealed class CarrierBookingRequestByNumberSpec : Specification<CarrierBookingRequest>
{
    public CarrierBookingRequestByNumberSpec(PersonaId personaId, string carrierBookingNumber)
    {
        Query.Where(request => request.PersonaId == personaId && request.CarrierBookingNumber == carrierBookingNumber)
            .AsNoTracking();
    }
}

using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The request a Carrier Persona answers, tracked so recording the answer saves it.</summary>
public sealed class CarrierBookingRequestToAnswerSpec : Specification<CarrierBookingRequest>
{
    public CarrierBookingRequestToAnswerSpec(PersonaId personaId, string carrierBookingNumber)
    {
        Query.Where(request => request.PersonaId == personaId && request.CarrierBookingNumber == carrierBookingNumber);
    }
}

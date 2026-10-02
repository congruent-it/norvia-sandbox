using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The answer settings a Carrier Persona was given, tracked so changing them saves them.</summary>
public sealed class CarrierAnswerSettingsOfPersonaSpec : SingleResultSpecification<CarrierAnswerSettings>
{
    public CarrierAnswerSettingsOfPersonaSpec(PersonaId personaId) => Query.Where(settings => settings.PersonaId == personaId);
}

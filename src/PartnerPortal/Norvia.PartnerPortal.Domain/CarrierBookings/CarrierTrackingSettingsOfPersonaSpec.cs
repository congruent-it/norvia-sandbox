using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The tracking settings a Carrier Persona was given, tracked so changing them saves them.</summary>
public sealed class CarrierTrackingSettingsOfPersonaSpec : SingleResultSpecification<CarrierTrackingSettings>
{
    public CarrierTrackingSettingsOfPersonaSpec(PersonaId personaId) => Query.Where(settings => settings.PersonaId == personaId);
}

using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Personas;

/// <param name="TrackingPollInterval">How often a Shipper Persona's tracking screen asks Norvia again while it is open.</param>
public sealed record PersonaView(string Id, string DisplayName, PartyType PartyType, TimeSpan? TrackingPollInterval = null)
{
    public static PersonaView From(Persona persona)
    {
        ArgumentNullException.ThrowIfNull(persona);
        return new(persona.Id.Value, persona.DisplayName, persona.PartyType, persona.PartyType == PartyType.Shipper ? persona.TrackingPollInterval : null);
    }
}

using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Personas;

public sealed record PersonaView(string Id, string DisplayName, PartyType PartyType)
{
    public static PersonaView From(Persona persona)
    {
        ArgumentNullException.ThrowIfNull(persona);
        return new(persona.Id.Value, persona.DisplayName, persona.PartyType);
    }
}

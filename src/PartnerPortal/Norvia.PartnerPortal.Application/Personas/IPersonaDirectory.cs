using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Personas;

/// <summary>The Personas the Partner portal plays, as the seed file names them.</summary>
public interface IPersonaDirectory
{
    /// <summary>Every Persona, in the seed file's order.</summary>
    IReadOnlyList<Persona> All { get; }

    /// <summary>The Persona with this ID, or null when there is none.</summary>
    Persona? Find(PersonaId id);
}

using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Application.Ports;

/// <summary>The profiles the Carriers write in, from the Simulator's own configuration (not the seed, ADR 0002), keyed by Carrier ID.</summary>
public interface IPersonaProfiles
{
    /// <summary>The profile a Carrier writes in, or null when it has none.</summary>
    PersonaProfile? ProfileOf(string carrierId);
}

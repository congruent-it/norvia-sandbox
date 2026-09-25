using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>A token for the Norvia Partner API, as the Persona's own client app (ADR 0005).</summary>
internal interface IPersonaTokens
{
    Task<string> GetTokenAsync(PersonaId personaId, CancellationToken cancellationToken);
}

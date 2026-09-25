using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// In Azure each Persona is an Entra client app that trusts the portal's managed identity (a federated credential), so
/// the portal asks for the Persona's token with a token of its own identity instead of a client secret (ADR 0005).
/// Each Persona's credential caches its token until it nearly expires.
/// </summary>
internal sealed class EntraPersonaTokens : IPersonaTokens
{
    private const string TokenExchangeScope = "api://AzureADTokenExchange/.default";

    private readonly PartnerApiOptions _options;
    private readonly EntraTokenOptions _entra;
    private readonly ManagedIdentityCredential _portalIdentity;
    private readonly ConcurrentDictionary<PersonaId, ClientAssertionCredential> _personas = new();

    public EntraPersonaTokens(IOptions<PartnerApiOptions> options)
    {
        _options = options.Value;
        _entra = _options.Entra ?? throw new InvalidOperationException("PartnerApi:Entra isn't configured.");
        _portalIdentity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(_entra.ManagedIdentityClientId));
    }

    public async Task<string> GetTokenAsync(PersonaId personaId, CancellationToken cancellationToken)
    {
        var persona = _personas.GetOrAdd(personaId, id => new ClientAssertionCredential(_entra.TenantId, _options.ClientIdOf(id), PortalIdentityTokenAsync));
        var token = await persona.GetTokenAsync(new TokenRequestContext([_entra.Scope]), cancellationToken);
        return token.Token;
    }

    private async Task<string> PortalIdentityTokenAsync(CancellationToken cancellationToken) =>
        (await _portalIdentity.GetTokenAsync(new TokenRequestContext([TokenExchangeScope]), cancellationToken)).Token;
}

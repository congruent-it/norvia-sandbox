using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Infrastructure.ErpApi;

namespace Norvia.Simulator.Infrastructure.Entra;

/// <summary>
/// In Azure Finance's token is the Simulator's own managed identity's, which the estate grants the ERP API's manage role.
/// The credential caches the token until it nearly expires.
/// </summary>
internal sealed class EntraErpTokens : IErpTokens
{
    private readonly ManagedIdentityCredential _identity;
    private readonly string _scope;

    public EntraErpTokens(IOptions<ErpApiOptions> options)
    {
        var entra = options.Value.Entra ?? throw new InvalidOperationException("ErpApi:Entra isn't configured.");
        _identity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entra.ManagedIdentityClientId));
        _scope = entra.Scope;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken) =>
        (await _identity.GetTokenAsync(new TokenRequestContext([_scope]), cancellationToken)).Token;
}

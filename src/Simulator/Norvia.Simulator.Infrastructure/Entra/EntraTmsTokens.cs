using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Infrastructure.TmsApi;

namespace Norvia.Simulator.Infrastructure.Entra;

/// <summary>
/// In Azure the Planner's token is the Simulator's own managed identity's, which the estate grants the TMS API's manage role.
/// The credential caches the token until it nearly expires.
/// </summary>
internal sealed class EntraTmsTokens : ITmsTokens
{
    private readonly ManagedIdentityCredential _identity;
    private readonly string _scope;

    public EntraTmsTokens(IOptions<TmsApiOptions> options)
    {
        var entra = options.Value.Entra ?? throw new InvalidOperationException("TmsApi:Entra isn't configured.");
        _identity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entra.ManagedIdentityClientId));
        _scope = entra.Scope;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken) =>
        (await _identity.GetTokenAsync(new TokenRequestContext([_scope]), cancellationToken)).Token;
}

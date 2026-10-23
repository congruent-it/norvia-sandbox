using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>
/// In Azure the portal is the presenter's gate (ADR 0024), so it switches the faults as its own managed identity, which the estate grants the
/// Demo.Fault role on each application that owns a fault. The credential caches the token until it nearly expires.
/// </summary>
internal sealed class EntraFaultTokens : IFaultTokens
{
    private readonly ManagedIdentityCredential _identity;

    public EntraFaultTokens(IOptions<FaultsOptions> options)
    {
        var entra = options.Value.Entra ?? throw new InvalidOperationException("Faults:Entra isn't configured.");
        _identity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entra.ManagedIdentityClientId));
    }

    public async Task<string> GetTokenAsync(FaultOwnerOptions owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (await _identity.GetTokenAsync(new TokenRequestContext([$"{owner.Audience}/.default"]), cancellationToken)).Token;
    }
}

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>
/// Locally there is no Entra, so the AppHost signs the presenter in: the portal signs the token itself with the key the applications trust, as Entra
/// would issue it to the portal's managed identity: its client ID in <c>azp</c> and the <c>Demo.Fault</c> app role.
/// </summary>
internal sealed class LocalFaultTokens(IOptions<FaultsOptions> options, TimeProvider time) : IFaultTokens
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public Task<string> GetTokenAsync(FaultOwnerOptions owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var local = options.Value.LocalTokens ?? throw new InvalidOperationException("Faults:LocalTokens isn't configured.");
        var now = time.GetUtcNow().UtcDateTime;
        return Task.FromResult(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = local.Issuer,
            Audience = owner.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = new Dictionary<string, object> { ["azp"] = local.ClientId, ["roles"] = new[] { FaultRoles.DemoFault } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(local.SigningKey)), SecurityAlgorithms.HmacSha256),
        }));
    }
}

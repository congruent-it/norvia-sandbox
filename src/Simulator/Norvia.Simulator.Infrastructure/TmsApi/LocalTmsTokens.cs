using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Simulator.Infrastructure.TmsApi;

/// <summary>
/// Locally there is no Entra, so the Simulator signs its token itself with the key the TMS trusts locally, as Entra would
/// issue it to the Simulator's managed identity: its client ID in <c>azp</c>, and the manage role the estate grants it.
/// </summary>
internal sealed class LocalTmsTokens(IOptions<TmsApiOptions> options, TimeProvider time) : ITmsTokens
{
    /// <summary>The TMS API's app role for changing Transport Orders as Norvia's staff do: the Planner's assignment.</summary>
    public const string ManageRole = "TransportOrders.Manage";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var local = options.Value.LocalTokens ?? throw new InvalidOperationException("TmsApi:LocalTokens isn't configured.");
        var now = time.GetUtcNow().UtcDateTime;
        return Task.FromResult(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = local.Issuer,
            Audience = local.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = new Dictionary<string, object> { ["azp"] = local.ClientId, ["roles"] = new[] { ManageRole } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(local.SigningKey)), SecurityAlgorithms.HmacSha256),
        }));
    }
}

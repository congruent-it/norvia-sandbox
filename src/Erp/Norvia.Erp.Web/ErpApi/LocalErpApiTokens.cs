using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Erp.Web.MasterData;

namespace Norvia.Erp.Web.ErpApi;

/// <summary>
/// Locally there is no Entra, so the ERP UI signs its token itself with the key the ERP API trusts locally, as Entra would issue
/// it to the ERP's managed identity: its client ID in <c>azp</c>, and the master-data role the estate grants it.
/// </summary>
internal sealed class LocalErpApiTokens(IOptions<ErpApiOptions> options, TimeProvider time) : IErpApiTokens
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var local = options.Value.LocalTokens ?? throw new InvalidOperationException("ErpApi:LocalTokens isn't configured.");
        var now = time.GetUtcNow().UtcDateTime;
        return Task.FromResult(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = local.Issuer,
            Audience = local.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = new Dictionary<string, object> { ["azp"] = local.ClientId, ["roles"] = new[] { MasterDataRoles.Manage } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(local.SigningKey)), SecurityAlgorithms.HmacSha256),
        }));
    }
}

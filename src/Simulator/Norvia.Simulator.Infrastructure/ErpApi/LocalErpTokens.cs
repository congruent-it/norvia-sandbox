using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Simulator.Infrastructure.ErpApi;

/// <summary>
/// Locally there is no Entra, so the Simulator signs its token itself with the key the ERP API trusts locally, as Entra would issue it to the Simulator's
/// managed identity: its client ID in <c>azp</c>, and the manage roles the estate grants it.
/// </summary>
internal sealed class LocalErpTokens(IOptions<ErpApiOptions> options, TimeProvider time) : IErpTokens
{
    /// <summary>The ERP API's app role for the Finance actions the Simulator plays: Invoice now and a Credit note.</summary>
    public const string ManageRole = "ShipperInvoices.Manage";

    /// <summary>The ERP API's app role for the Finance decisions on Carrier invoice lines the Simulator plays.</summary>
    public const string CarrierInvoicesManageRole = "CarrierInvoices.Manage";

    /// <summary>The ERP API's app role for the reset door.</summary>
    public const string ResetRole = "Estate.Reset";

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
            Claims = new Dictionary<string, object> { ["azp"] = local.ClientId, ["roles"] = new[] { ManageRole, CarrierInvoicesManageRole, ResetRole } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(local.SigningKey)), SecurityAlgorithms.HmacSha256),
        }));
    }
}

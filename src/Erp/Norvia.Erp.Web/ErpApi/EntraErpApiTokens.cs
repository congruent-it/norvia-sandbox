using Azure.Core;

namespace Norvia.Erp.Web.ErpApi;

/// <summary>
/// In Azure the ERP UI asks Entra for its token as its own managed identity, which the estate grants the ERP API's
/// <c>MasterData.Manage</c> role. The credential caches the token until it nearly expires.
/// </summary>
internal sealed class EntraErpApiTokens(TokenCredential identity, string scope) : IErpApiTokens
{
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken) =>
        (await identity.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)).Token;
}

using Azure.Core;

namespace Norvia.Tms.Web.TmsApi;

/// <summary>
/// In Azure the TMS UI asks Entra for its token as its own managed identity, which the estate grants the TMS API's
/// manage role. The credential caches the token until it nearly expires.
/// </summary>
/// <param name="identity">The TMS UI's managed identity.</param>
/// <param name="scope">The TMS API's scope: <c>&lt;its client ID&gt;/.default</c>.</param>
internal sealed class EntraTmsApiTokens(TokenCredential identity, string scope) : ITmsApiTokens
{
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken) =>
        (await identity.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)).Token;
}

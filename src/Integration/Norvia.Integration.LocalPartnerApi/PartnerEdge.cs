using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// What API Management checks at the edge, in the policy's order (infra/estate/partner-api/submit-transport-order.policy.xml):
/// the subscription key, a token for the Norvia Partner API, the operation's role, and a party for the token's client.
/// The Shipper or Carrier comes from the token, never from the payload (ADR 0005). The checks are the operation's
/// <see cref="EdgeRule"/>; infra/estate/partner-api/ holds the policies they play.
/// </summary>
internal sealed class PartnerEdge(IOptions<PartnerApiOptions> options)
{
    private readonly JsonWebTokenHandler _tokens = new();

    public async Task<EdgeCheck> CheckAsync(HttpRequest request, EdgeRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rule);
        cancellationToken.ThrowIfCancellationRequested();
        var partnerApi = options.Value;
        var key = request.Headers["Ocp-Apim-Subscription-Key"].ToString();
        if (string.IsNullOrEmpty(key))
        {
            return EdgeCheck.Unauthorized("Access denied due to missing subscription key. Make sure to include subscription key when making requests to an API.");
        }

        if (!partnerApi.SubscriptionKeys.ContainsValue(key))
        {
            return EdgeCheck.Unauthorized("Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription.");
        }

        var authorization = request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization["Bearer ".Length..].Trim() : string.Empty;
        var validation = await _tokens.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = partnerApi.Tokens.Issuer,
            ValidAudience = partnerApi.Tokens.Audience,
            IssuerSigningKey = partnerApi.Tokens.Key,
        });
        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken partnerToken)
        {
            return EdgeCheck.Unauthorized("Unauthorized. Access token is missing or invalid.");
        }

        if (!partnerToken.Claims.Any(claim => claim.Type == "roles" && claim.Value == rule.Role))
        {
            return EdgeCheck.Forbidden(rule.NotAllowedText, rule.NotAllowedCode);
        }

        return partnerToken.TryGetClaim("azp", out var client) && rule.PartiesByClientId(partnerApi).GetValueOrDefault(client.Value) is { } partyId
            ? EdgeCheck.Passed(partyId)
            : EdgeCheck.Forbidden(rule.NoPartyText, rule.NoPartyCode);
    }
}

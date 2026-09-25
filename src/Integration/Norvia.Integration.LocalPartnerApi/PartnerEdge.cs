using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// What API Management checks at the edge, in the policy's order (infra/estate/partner-api/submit-transport-order.policy.xml):
/// the subscription key, a token for the Norvia Partner API, the role to submit, and a Shipper for the token's client.
/// The Shipper comes from the token, never from the booking (ADR 0005).
/// </summary>
internal sealed class PartnerEdge(IOptions<PartnerApiOptions> options)
{
    public const string SubmitRole = "TransportOrders.Submit";

    private readonly JsonWebTokenHandler _tokens = new();

    public async Task<EdgeCheck> CheckAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        if (!partnerToken.Claims.Any(claim => claim.Type == "roles" && claim.Value == SubmitRole))
        {
            return EdgeCheck.Forbidden("This client may not submit Transport Orders.", "NotAllowedToSubmit");
        }

        return partnerToken.TryGetClaim("azp", out var client) && partnerApi.ShippersByClientId.GetValueOrDefault(client.Value) is { } shipperId
            ? EdgeCheck.Passed(shipperId)
            : EdgeCheck.Forbidden("No Shipper is registered for this client.", "NoShipperForClient");
    }
}

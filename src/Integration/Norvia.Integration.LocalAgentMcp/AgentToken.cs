using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp;

/// <summary>An Agent's token once validated: who is calling (<c>azp</c>) and the app roles it holds.</summary>
internal sealed record AgentToken(string? AgentId, IReadOnlySet<string> Roles)
{
    /// <summary>Null when the token is missing, expired, signed with another key or meant for another audience.</summary>
    public static async Task<AgentToken?> ValidateAsync(string authorization, LocalTokenOptions tokens)
    {
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization["Bearer ".Length..].Trim() : string.Empty;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = tokens.Issuer,
            ValidAudience = tokens.Audience,
            IssuerSigningKey = tokens.Key,
        });

        return result is { IsValid: true, SecurityToken: JsonWebToken jwt }
            ? new AgentToken(
                jwt.TryGetClaim("azp", out var azp) && !string.IsNullOrEmpty(azp.Value) ? azp.Value : null,
                jwt.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal))
            : null;
    }
}

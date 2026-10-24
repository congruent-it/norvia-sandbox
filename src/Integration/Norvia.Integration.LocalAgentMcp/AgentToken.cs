using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalAgentMcp;

/// <summary>An Agent's token once validated: who is calling (<c>azp</c>) and the app roles it holds.</summary>
internal sealed record AgentToken(string? AgentId, IReadOnlySet<string> Roles)
{
    /// <summary>Null when the token is missing, expired, signed with another key or meant for another audience.</summary>
    public static async Task<AgentToken?> ValidateAsync(string authorization, AgentMcpOptions options)
    {
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization["Bearer ".Length..].Trim() : string.Empty;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Tokens.Issuer,
            ValidAudience = options.Tokens.Audience,
            IssuerSigningKey = options.Tokens.Key,
        });

        return result is { IsValid: true, SecurityToken: JsonWebToken jwt }
            ? new AgentToken(
                jwt.TryGetClaim("azp", out var azp) && !string.IsNullOrEmpty(azp.Value) ? azp.Value : null,
                jwt.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal))
            : null;
    }
}

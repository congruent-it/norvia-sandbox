using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.IntegrationConsole.Infrastructure.Agents;

/// <summary>An Agent's token as Entra issues it to its agent identity, signed with the AppHost's local key (ADR 0041).</summary>
internal static class AgentToken
{
    public static string Create(AgentTokenOptions tokens, TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = tokens.Issuer,
            Audience = tokens.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["azp"] = tokens.ClientId, ["roles"] = new[] { tokens.Role } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(tokens.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
    }
}

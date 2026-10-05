using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalSupport;

/// <summary>A token as Entra issues it to a Logic App's managed identity for the TMS, signed with the AppHost's local key.</summary>
public static class TmsTokens
{
    public static string Create(LocalTokenOptions tokens, TimeProvider time, string clientId, string role)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = tokens.Issuer,
            Audience = tokens.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = new[] { role } },
            SigningCredentials = new SigningCredentials(tokens.Key, SecurityAlgorithms.HmacSha256),
        });
    }
}

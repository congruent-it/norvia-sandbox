using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;

namespace Norvia.Erp.TestSupport;

/// <summary>Tokens for the ERP API, signed with the key the AppHost trusts, as the Logic App identities holding a role call it.</summary>
public static class ErpTokens
{
    /// <summary>The identity of a Logic App that has no client ID of its own in <see cref="LocalTokens"/>.</summary>
    public const string TestLogicApp = "norvia-test-logic-app";

    public static string Create(string signingKey, string role, string clientId) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.ErpAudience,
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = new[] { role } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        });
}

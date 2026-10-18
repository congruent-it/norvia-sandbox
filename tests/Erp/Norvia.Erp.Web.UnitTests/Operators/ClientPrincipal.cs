using System.Text;
using System.Text.Json;

using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.Web.UnitTests.Operators;

/// <summary>The X-MS-CLIENT-PRINCIPAL header as Container Apps' Easy Auth sends it for an Entra sign-in.</summary>
internal static class ClientPrincipal
{
    public static string Of(string name, params string[] roles) =>
        Encode(new
        {
            auth_typ = EasyAuthHandler.EntraAuthenticationType,
            name_typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
            role_typ = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            claims = new[]
            {
                new { typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", val = name },
                new { typ = "preferred_username", val = "user@norvia.example" },
            }.Concat(roles.Select(role => new { typ = "roles", val = role })),
        });

    public static string Encode(object principal) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(principal)));
}

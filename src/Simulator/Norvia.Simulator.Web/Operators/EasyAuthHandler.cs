using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Norvia.Simulator.Web.Operators;

/// <summary>
/// Reads the operator Container Apps' Easy Auth signed in from the X-MS-CLIENT-PRINCIPAL header it adds to every
/// request. Container Apps doesn't let a request from outside set that header, so it holds only what Easy Auth put there.
/// </summary>
internal sealed class EasyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : OperatorAuthenticationHandler(options, logger, encoder)
{
    public const string SchemeName = "EasyAuth";
    public const string ClientPrincipalHeader = "X-MS-CLIENT-PRINCIPAL";

    /// <summary>The authentication type Easy Auth gives an operator it signed in with Entra ID.</summary>
    public const string EntraAuthenticationType = "aad";

    /// <summary>Easy Auth's endpoint that signs the browser in to Entra ID and sends it back.</summary>
    public const string SignInPath = "/.auth/login/aad";

    // Entra sends app roles as "roles", which Easy Auth passes on under that name rather than as its own role_typ.
    private const string EntraRoles = "roles";
    private const string EntraName = "name";
    private const string EntraUsername = "preferred_username";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ClientPrincipalHeader, out var header) || string.IsNullOrEmpty(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = Decode(header.ToString());
        return Task.FromResult(principal is null
            ? AuthenticateResult.Fail($"The {ClientPrincipalHeader} header holds no client principal.")
            : AuthenticateResult.Success(new AuthenticationTicket(ToClaimsPrincipal(principal), SchemeName)));
    }

    /// <summary>Easy Auth normally signs the browser in before the request gets here; this covers a session that ended.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var backTo = Request.PathBase + Request.Path + Request.QueryString;
        Response.Redirect($"{SignInPath}?post_login_redirect_uri={Uri.EscapeDataString(backTo)}");
        return Task.CompletedTask;
    }

    private static EasyAuthPrincipal? Decode(string header)
    {
        try
        {
            return JsonSerializer.Deserialize<EasyAuthPrincipal>(Convert.FromBase64String(header));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static ClaimsPrincipal ToClaimsPrincipal(EasyAuthPrincipal principal)
    {
        var claims = principal.Claims
            .Select(claim => claim.Type == EntraRoles || claim.Type == principal.RoleType
                ? new Claim(ClaimTypes.Role, claim.Value)
                : new Claim(claim.Type, claim.Value))
            .ToList();

        // The name as Easy Auth says to find it, else as Entra puts it in the ID token.
        var nameType = new[] { principal.NameType, EntraName, EntraUsername }
            .FirstOrDefault(type => type is not null && claims.Exists(claim => claim.Type == type)) ?? EntraName;

        return new ClaimsPrincipal(new ClaimsIdentity(claims, principal.AuthenticationType, nameType, ClaimTypes.Role));
    }

    private sealed record EasyAuthPrincipal(
        [property: JsonPropertyName("auth_typ")] string AuthenticationType,
        [property: JsonPropertyName("claims")] IReadOnlyList<EasyAuthClaim> Claims,
        [property: JsonPropertyName("name_typ")] string? NameType,
        [property: JsonPropertyName("role_typ")] string? RoleType);

    private sealed record EasyAuthClaim(
        [property: JsonPropertyName("typ")] string Type,
        [property: JsonPropertyName("val")] string Value);
}

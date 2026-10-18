using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Norvia.Erp.Web.Operators;

/// <summary>Signs every request in as the local user, with the roles the AppHost gives them. A role missing sends them to the refusal.</summary>
internal sealed class LocalUserHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<LocalUserOptions> localUser)
    : UserAuthenticationHandler(options, logger, encoder)
{
    public const string SchemeName = "LocalUser";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = localUser.Value.Roles.Select(role => new Claim(ClaimTypes.Role, role))
            .Prepend(new Claim(ClaimTypes.Name, localUser.Value.Name));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

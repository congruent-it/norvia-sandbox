using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Norvia.IntegrationConsole.Web.Operators;

/// <summary>Signs every request in as the local operator, with the roles the AppHost gives them.</summary>
internal sealed class LocalOperatorHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<LocalOperatorOptions> localOperator)
    : OperatorAuthenticationHandler(options, logger, encoder)
{
    public const string SchemeName = "LocalOperator";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = localOperator.Value.Roles.Select(role => new Claim(ClaimTypes.Role, role))
            .Prepend(new Claim(ClaimTypes.Name, localOperator.Value.Name));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

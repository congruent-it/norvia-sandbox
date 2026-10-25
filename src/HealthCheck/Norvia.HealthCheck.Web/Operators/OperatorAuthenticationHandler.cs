using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Norvia.HealthCheck.Web.Operators;

/// <summary>What both ways of signing in do with an operator who lacks the role a page needs: show them the refusal.</summary>
internal abstract class OperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string RefusalPath = "/refused";

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect(RefusalPath);
        return Task.CompletedTask;
    }
}

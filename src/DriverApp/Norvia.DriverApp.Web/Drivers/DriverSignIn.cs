using System.Net;
using System.Security.Claims;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Norvia.DriverApp.Application.Drivers.List;

namespace Norvia.DriverApp.Web.Drivers;

/// <summary>
/// How a Driver signs in (ADR 0037). In Azure, Container Apps' Easy Auth signs them in to the Driver app's app registration in
/// Entra ID, and their token carries the Driver ID. Locally there is no Easy Auth, so a stand-in plays Entra: the AppHost
/// turns it on, it lists the seeded Drivers on a page of its own, and the Driver it signs in carries the same Driver ID claim.
/// </summary>
public static class DriverSignIn
{
    public const string RefusalPath = "/refused";
    public const string StandInSignInPath = "/local/sign-in";
    public const string StandInSignOutPath = "/local/sign-out";
    public const string StandInAuthenticationType = "LocalDriver";
    public const string StandInSettingsSection = "LocalSignIn";

    private const string StandInCookie = "norvia-driver-local";

    // Long enough that a demo phone stays with its Driver between demos.
    private static readonly TimeSpan StandInLifetime = TimeSpan.FromDays(365);

    public static bool IsStandIn(this IConfiguration configuration) =>
        configuration.GetValue<bool>($"{StandInSettingsSection}:Enabled");

    public static IHostApplicationBuilder AddDriverSignIn(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Configuration.IsStandIn())
        {
            // Anywhere else, the stand-in would let anyone in as any Driver.
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"The sign-in stand-in ({StandInSettingsSection}) runs only in Development; in Azure Easy Auth signs Drivers in.");
            }

            builder.Services.AddAuthentication(StandInAuthenticationType)
                .AddCookie(StandInAuthenticationType, options =>
                {
                    options.Cookie.Name = StandInCookie;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.IsEssential = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = StandInLifetime;
                    options.LoginPath = StandInSignInPath;
                    options.AccessDeniedPath = RefusalPath;
                });
        }
        else
        {
            builder.Services.AddAuthentication(EasyAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, EasyAuthHandler>(EasyAuthHandler.SchemeName, configureOptions: null);
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(DriverClaims.DriverPolicy, policy => policy.RequireAuthenticatedUser()
                // Exactly one: a user holding two Driver roles would otherwise be an arbitrary one of them.
                .RequireAssertion(context => context.User.FindAll(DriverClaims.DriverId).Count() == 1));
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }

    /// <summary>Maps the stand-in's sign-in page, when there is one. Elsewhere there is nothing to map.</summary>
    public static IEndpointRouteBuilder MapDriverSignInStandIn(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.IsStandIn())
        {
            return app;
        }

        // The stand-in is a Development-only stand-in for Entra's own sign-in page, so it is plain HTML rather than a Blazor page
        // that would exist in Azure too, and its posts carry no antiforgery token, which Entra's page has none of either.
        var standIn = app.MapGroup(string.Empty).AllowAnonymous().DisableAntiforgery();

        standIn.MapGet(StandInSignInPath, async (IMediator mediator, string? returnUrl, CancellationToken cancellationToken) =>
        {
            var drivers = await mediator.Send(new ListDriversQuery(), cancellationToken);
            var page = new StringBuilder("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />")
                .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" /><title>Sign in</title>")
                .Append("<link rel=\"stylesheet\" href=\"/app.css\" /></head><body><main class=\"app-main\"><h1>Sign in as a Driver</h1>")
                .Append("<p>This stands in for Entra ID, which signs Drivers in when the Driver app runs in Azure. Pick a seeded Driver.</p>")
                .Append("<form method=\"post\" action=\"").Append(StandInSignInPath).Append("\" aria-label=\"Sign in\" class=\"driver-list\">")
                .Append("<input type=\"hidden\" name=\"returnUrl\" value=\"").Append(WebUtility.HtmlEncode(returnUrl ?? "/")).Append("\" />");
            foreach (var driver in drivers)
            {
                page.Append("<button type=\"submit\" name=\"driver\" value=\"").Append(WebUtility.HtmlEncode(driver.Id)).Append("\" class=\"driver-button\">")
                    .Append("<span class=\"driver-name\">").Append(WebUtility.HtmlEncode(driver.Name)).Append("</span>")
                    .Append("<span class=\"driver-id\">").Append(WebUtility.HtmlEncode(driver.Id)).Append("</span></button>");
            }

            page.Append("</form></main></body></html>");
            return Results.Content(page.ToString(), "text/html", Encoding.UTF8);
        });

        standIn.MapPost(StandInSignInPath, async ([FromForm] string driver, [FromForm] string? returnUrl, HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var drivers = await mediator.Send(new ListDriversQuery(), cancellationToken);
            if (drivers.FirstOrDefault(known => known.Id == driver) is not { } chosen)
            {
                // With a body, so the status code pages don't turn it into the Not Found page.
                return Results.Problem($"There is no Driver '{driver}'.", statusCode: StatusCodes.Status400BadRequest, title: "Unknown Driver");
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, chosen.Name), new Claim(DriverClaims.DriverId, chosen.Id)], StandInAuthenticationType);
            await context.SignInAsync(StandInAuthenticationType, new ClaimsPrincipal(identity));
            return Results.LocalRedirect(LocalOrHome(returnUrl));
        });

        standIn.MapPost(StandInSignOutPath, async (HttpContext context) =>
        {
            await context.SignOutAsync(StandInAuthenticationType);
            return Results.Redirect(StandInSignInPath);
        });

        return app;
    }

    /// <summary>Where to go after signing in: back to where the browser was, if that is a path on this app, else home.</summary>
    private static string LocalOrHome(string? returnUrl) =>
        string.IsNullOrEmpty(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) || returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? "/"
            : returnUrl;
}

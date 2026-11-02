using System.Net;
using System.Security.Claims;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Norvia.DriverApp.Application.Drivers.List;

namespace Norvia.DriverApp.Web.Drivers;

/// <summary>
/// The presenter's Driver picker (ADR 0047). Easy Auth still signs the presenter in, to the demo UIs' registration, so strangers
/// stay out and nobody needs a Driver test user with multi-factor authentication. The presenter then picks which seeded Driver
/// they are, and the Driver ID goes into a cookie that turns into the same Driver ID claim the Driver's own token would carry.
/// It is only ever honoured for a user Easy Auth has signed in.
/// </summary>
public static class DriverPicker
{
    public const string PickerPath = "/pick-driver";
    public const string SettingsSection = "DriverPicker";
    public const string CookieName = "norvia-driver-picked";

    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);

    public static bool IsPicker(this IConfiguration configuration) =>
        configuration.GetValue<bool>($"{SettingsSection}:Enabled");

    /// <summary>Turns the Driver the presenter picked into a Driver ID claim, for a user who has none of their own.</summary>
    internal sealed class PickedDriverClaims(IHttpContextAccessor accessor, IMediator mediator) : IClaimsTransformation
    {
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var context = accessor.HttpContext;
            if (principal.Identity?.IsAuthenticated != true
                || principal.HasClaim(claim => claim.Type == DriverClaims.DriverId)
                || context is null
                || !context.Request.Cookies.TryGetValue(CookieName, out var picked)
                || string.IsNullOrEmpty(picked))
            {
                return principal;
            }

            var drivers = await mediator.Send(new ListDriversQuery(), context.RequestAborted);
            if (drivers.FirstOrDefault(known => known.Id == picked) is not { } driver)
            {
                return principal;
            }

            var transformed = new ClaimsPrincipal(principal.Identities);
            transformed.AddIdentity(new ClaimsIdentity(
                [new Claim(DriverClaims.DriverId, driver.Id), new Claim(DriverClaims.DriverName, driver.Name)],
                principal.Identity.AuthenticationType));
            return transformed;
        }
    }

    /// <summary>Maps the picker's page, when there is one. Elsewhere there is nothing to map.</summary>
    public static IEndpointRouteBuilder MapDriverPicker(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.IsPicker())
        {
            return app;
        }

        // Plain HTML, like the local stand-in's page: it isn't a screen of a Driver's, so the Driver policy doesn't apply, but
        // the presenter has to be signed in. Its post carries the antiforgery token the page was rendered with, which the antiforgery middleware checks.
        var picker = app.MapGroup(string.Empty).RequireAuthorization(policy => policy.RequireAuthenticatedUser());

        picker.MapGet(PickerPath, async (IMediator mediator, IAntiforgery antiforgery, HttpContext context, string? returnUrl, CancellationToken cancellationToken) =>
        {
            var drivers = await mediator.Send(new ListDriversQuery(), cancellationToken);
            var tokens = antiforgery.GetAndStoreTokens(context);
            var page = new StringBuilder("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />")
                .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" /><title>Pick a Driver</title>")
                .Append(DriverSignIn.IconLinks)
                .Append("<link rel=\"stylesheet\" href=\"/app.css\" /></head><body><main class=\"app-main\"><h1>Who is driving?</h1>")
                .Append("<p>You are signed in as the presenter. Pick the seeded Driver to act as.</p>")
                .Append("<form method=\"post\" action=\"").Append(PickerPath).Append("\" aria-label=\"Pick a Driver\" class=\"driver-list\">")
                .Append("<input type=\"hidden\" name=\"").Append(WebUtility.HtmlEncode(tokens.FormFieldName)).Append("\" value=\"")
                .Append(WebUtility.HtmlEncode(tokens.RequestToken)).Append("\" />")
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

        picker.MapPost(PickerPath, async ([FromForm] string driver, [FromForm] string? returnUrl, HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var drivers = await mediator.Send(new ListDriversQuery(), cancellationToken);
            if (drivers.All(known => known.Id != driver))
            {
                // With a body, so the status code pages don't turn it into the Not Found page.
                return Results.Problem($"There is no Driver '{driver}'.", statusCode: StatusCodes.Status400BadRequest, title: "Unknown Driver");
            }

            context.Response.Cookies.Append(CookieName, driver, new CookieOptions
            {
                HttpOnly = true,
                // Easy Auth makes the browser use HTTPS, but Container Apps ends it before the request gets here.
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = CookieLifetime,
            });
            return Results.LocalRedirect(DriverSignIn.LocalOrHome(returnUrl));
        });

        return app;
    }
}

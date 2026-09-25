using Microsoft.AspNetCore.Mvc;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.Personas;

/// <summary>
/// The Persona switcher's endpoints: the portal's home opens the current Persona's booking form, and switching
/// remembers the new Persona in a cookie and opens its booking form. No login: the portal plays every party in a demo.
/// </summary>
public static class PersonaEndpoints
{
    // Long enough that a demo machine stays with its Persona between demos.
    private static readonly TimeSpan Remembered = TimeSpan.FromDays(365);

    public static IEndpointRouteBuilder MapPersonaSwitcher(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal: it's a demo application without a login, and who it acts as towards
        // Norvia is the Persona's own token, not the browser's.
        endpoints.MapGet("/", OpenHomeAsync).AllowAnonymous();
        // A form post, so the Persona switcher works before the page is interactive; antiforgery applies to form binding.
        endpoints.MapPost("/persona", ([FromForm] string persona, HttpResponse response, IPartnerPortal portal) => SwitchAsync(persona, response, portal))
            .AllowAnonymous();
        return endpoints;
    }

    public static async Task<IResult> OpenHomeAsync(HttpRequest request, IPartnerPortal portal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(portal);
        var personas = await portal.ListPersonasAsync(request.HttpContext.RequestAborted);
        return TypedResults.Redirect(PersonaChoice.NewBookingPath(PersonaChoice.Current(request, personas).Id));
    }

    public static async Task<IResult> SwitchAsync(string persona, HttpResponse response, IPartnerPortal portal)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(portal);
        if (PersonaChoice.Find(persona, await portal.ListPersonasAsync(response.HttpContext.RequestAborted)) is not { } chosen)
        {
            // With a body, so the status code pages don't turn it into the Not Found page.
            return TypedResults.Problem($"There is no Persona '{persona}'.", statusCode: StatusCodes.Status400BadRequest, title: "Unknown Persona");
        }

        response.Cookies.Append(PersonaChoice.CookieName, chosen.Id, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = response.HttpContext.Request.IsHttps,
            MaxAge = Remembered,
        });
        // A redirect after the post, so the browser opens the form with a GET and a reload doesn't post again.
        return TypedResults.Redirect(PersonaChoice.NewBookingPath(chosen.Id));
    }
}

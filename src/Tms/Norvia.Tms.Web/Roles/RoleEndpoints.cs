using Microsoft.AspNetCore.Mvc;

namespace Norvia.Tms.Web.Roles;

/// <summary>
/// The Role switcher's endpoints: the TMS UI's home opens the current role's view, and switching remembers the new role
/// in a cookie and opens its view. No login: the role only chooses what the UI shows first.
/// </summary>
public static class RoleEndpoints
{
    // Long enough that a demo machine stays in its role between demos.
    private static readonly TimeSpan Remembered = TimeSpan.FromDays(365);

    public static IEndpointRouteBuilder MapRoleSwitcher(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the TMS UI: it's a demo application without a login (issue #22), and the role
        // chooses only what the UI shows first, not what a user may do.
        endpoints.MapGet("/", OpenHome).AllowAnonymous();
        // A form post, so the Role switcher works before the page is interactive; antiforgery applies to form binding.
        endpoints.MapPost("/role", ([FromForm] string role, HttpResponse response) => Switch(role, response)).AllowAnonymous();
        return endpoints;
    }

    public static IResult OpenHome(HttpRequest request) => TypedResults.Redirect(NorviaRole.Current(request).View);

    public static IResult Switch(string role, HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (NorviaRole.Find(role) is not { } chosen)
        {
            // With a body, so the status code pages don't turn it into the Not Found page.
            return TypedResults.Problem($"There is no role '{role}'.", statusCode: StatusCodes.Status400BadRequest, title: "Unknown role");
        }

        response.Cookies.Append(NorviaRole.CookieName, chosen.Key, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = response.HttpContext.Request.IsHttps,
            MaxAge = Remembered,
        });
        // A redirect after the post, so the browser opens the view with a GET and a reload doesn't post again.
        return TypedResults.Redirect(chosen.View);
    }
}

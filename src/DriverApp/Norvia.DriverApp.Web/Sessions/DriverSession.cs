using Norvia.DriverApp.Application.Drivers;

namespace Norvia.DriverApp.Web.Sessions;

/// <summary>
/// Which Driver is signed in: the one picked on the sign-in page, remembered in a cookie. There is no password; picking a
/// seeded Driver is the whole sign-in until the Driver app gets Entra ID (P12).
/// </summary>
public static class DriverSession
{
    public const string CookieName = "norvia-driver";

    // Long enough that a demo phone stays with its Driver between demos.
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    public static DriverView? Current(HttpContext? context, IReadOnlyList<DriverView> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        return Find(drivers, context?.Request.Cookies[CookieName]);
    }

    public static DriverView? Find(IReadOnlyList<DriverView> drivers, string? driverId)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        return drivers.FirstOrDefault(driver => driver.Id == driverId);
    }

    public static void SignIn(HttpResponse response, DriverView driver)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(driver);
        response.Cookies.Append(CookieName, driver.Id, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = response.HttpContext.Request.IsHttps,
            MaxAge = CookieLifetime,
        });
    }

    public static void SignOut(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(CookieName);
    }
}

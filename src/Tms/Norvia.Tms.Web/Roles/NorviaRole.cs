namespace Norvia.Tms.Web.Roles;

/// <summary>
/// A role a Norvia user takes in the TMS UI, chosen with the Role switcher rather than by logging in, so a demo moves
/// quickly. Each role opens on its own view.
/// </summary>
/// <param name="Key">The stable name the role is remembered and switched by.</param>
/// <param name="Name">The role as the Role switcher names it.</param>
/// <param name="View">Where the role opens.</param>
/// <param name="ViewTitle">The role's view as the navigation names it.</param>
public sealed record NorviaRole(string Key, string Name, string View, string ViewTitle)
{
    /// <summary>The cookie that remembers the role a browser last switched to.</summary>
    public const string CookieName = "norvia-tms-role";

    public static readonly NorviaRole Planner = new("planner", "Planner", "/transport-orders", "Waiting for planning");

    public static readonly NorviaRole CustomerService = new("customer-service", "Customer service", "/rejected-bookings", "Rejected bookings");

    /// <summary>Every role, the one a browser starts in first.</summary>
    public static readonly IReadOnlyList<NorviaRole> All = [Planner, CustomerService];

    /// <summary>The role with this key, or null when there is none.</summary>
    public static NorviaRole? Find(string? key) => All.FirstOrDefault(role => role.Key == key);

    /// <summary>The role the browser last switched to, or the Planner when it hasn't.</summary>
    public static NorviaRole Current(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Find(request.Cookies[CookieName]) ?? Planner;
    }
}

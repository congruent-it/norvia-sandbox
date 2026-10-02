namespace Norvia.Tms.Web.Roles;

/// <summary>
/// A role a Norvia user takes in the TMS UI, chosen with the Role switcher rather than by logging in, so a demo moves
/// quickly. Each role has views of its own, and opens on the first.
/// </summary>
/// <param name="Key">The stable name the role is remembered and switched by.</param>
/// <param name="Name">The role as the Role switcher names it.</param>
/// <param name="Views">The role's views, as the main navigation lists them; there is at least one.</param>
public sealed record NorviaRole(string Key, string Name, IReadOnlyList<RoleView> Views)
{
    /// <summary>The cookie that remembers the role a browser last switched to.</summary>
    public const string CookieName = "norvia-tms-role";

    public static readonly NorviaRole Planner = new("planner", "Planner",
        [
            new("/transport-orders", "Waiting for planning"),
            new("/shipments/to-release", "Shipments to release"),
            new("/shipments/awaiting-answer", "Bookings awaiting an answer"),
        ]);

    public static readonly NorviaRole CustomerService = new("customer-service", "Customer service",
        [new("/rejected-bookings", "Rejected bookings"), new("/rating-exceptions", "Rating exceptions")]);

    /// <summary>Where the role opens: its first view.</summary>
    public string Home => Views[0].Path;

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

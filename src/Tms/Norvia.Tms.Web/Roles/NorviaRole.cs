namespace Norvia.Tms.Web.Roles;

/// <summary>
/// A role a Norvia user takes in the TMS UI, chosen with the Role switcher rather than by logging in, so a demo moves
/// quickly. Each role has views of its own, and opens on its home page when it has one, otherwise on the first view.
/// </summary>
/// <param name="Key">The stable name the role is remembered and switched by.</param>
/// <param name="Name">The role as the Role switcher names it.</param>
/// <param name="Views">The role's views, as the main navigation lists them; there is at least one.</param>
/// <param name="HomePath">Where the role's home page opens, or null when the role opens on its first view.</param>
public sealed record NorviaRole(string Key, string Name, IReadOnlyList<RoleView> Views, string? HomePath = null)
{
    /// <summary>The cookie that remembers the role a browser last switched to.</summary>
    public const string CookieName = "norvia-tms-role";

    /// <summary>Where the Planner's home page opens: the work to act on, in front of the views grouped by what the Planner does with them.</summary>
    public const string PlannerHomePath = "/home";

    public static readonly NorviaRole Planner = new("planner", "Planner",
        [
            new("/transport-orders", "Waiting for planning", "Plan"),
            new("/transport-orders/all", "Transport Orders", "Plan"),
            new("/shipments/to-release", "Shipments to release", "Plan"),
            new("/shipments/awaiting-answer", "Bookings awaiting an answer", "Follow"),
            new("/shipments/in-progress", "Shipments in progress", "Follow"),
            new("/shipments/delivered-without-proof-of-delivery", "Delivered without Proof of delivery", "Follow"),
            new("/exceptions", "Open Exceptions", "Follow"),
            new("/capacity", "Capacity", "Own fleet"),
            new("/trips/shipments", "Shipments to put on Trips", "Own fleet"),
            new("/trips", "Trips", "Own fleet"),
        ],
        PlannerHomePath);

    public static readonly NorviaRole CustomerService = new("customer-service", "Customer service",
        [new("/rejected-bookings", "Rejected bookings"), new("/transport-orders/all", "Transport Orders"), new("/rating-exceptions", "Rating exceptions"), new("/not-yet-billable", "Not yet billable")]);

    /// <summary>Where the role opens: its home page, or its first view when it has none.</summary>
    public string Home => HomePath ?? Views[0].Path;

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

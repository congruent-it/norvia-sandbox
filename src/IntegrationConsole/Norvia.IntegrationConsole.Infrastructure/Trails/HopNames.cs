namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// What a hop is called on its trail, after its application and what happened there. Both trail sources name hops
/// alike, so a trail reads the same locally as in Azure.
/// </summary>
internal static class HopNames
{
    private static readonly HashSet<string> Methods =
        new(["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE", "CONNECT"], StringComparer.Ordinal);

    /// <summary>
    /// A call, by its method and the path actually called, such as <c>tms-api: POST /v1/transport-orders/TO-0000042/assign</c>.
    /// ASP.NET Core names a request after its route template, placeholders and all, to keep span names low in
    /// cardinality; the console shows one trail at a time, so the business key in the path is what it wants. A call with
    /// no path, or whose method is neither recorded nor at the start of its name, such as API Management's operation, keeps
    /// its name.
    /// </summary>
    public static string OfCall(string application, string name, string? method, string? path)
    {
        var called = method is { Length: > 0 } ? method : name.Split(' ', 2)[0];
        return path is { Length: > 0 } && Methods.Contains(called)
            ? $"{application}: {called} {path}"
            : $"{application}: {name}";
    }

    /// <summary>
    /// What an application recorded outside any call: the event it published, such as
    /// <c>tms-api: publish ShipmentPlanned</c>, when its records say which, or else only the application.
    /// </summary>
    public static string OfRecords(string application, string? eventType) =>
        eventType is { Length: > 0 } ? $"{application}: publish {eventType}" : application;
}

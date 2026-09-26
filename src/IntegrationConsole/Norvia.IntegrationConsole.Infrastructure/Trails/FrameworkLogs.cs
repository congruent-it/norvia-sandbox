namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// The logging categories of the frameworks an application runs on, such as the Azure SDK, ASP.NET Core and
/// HttpClient. They log in the application's logging scope, so their records carry the tracking contract's keys too,
/// but they are no hop of the trail's: an application's own records are.
/// </summary>
internal static class FrameworkLogs
{
    private static readonly string[] Prefixes = ["Azure.", "Microsoft.", "System."];

    public static bool Wrote(string? category) =>
        category is not null && Prefixes.Any(prefix => category.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The KQL that is true for a record none of them wrote, given its category's column.</summary>
    public static string NoneWrote(string category) =>
        $"not({string.Join(" or ", Prefixes.Select(prefix => $"{category} startswith \"{prefix}\""))})";
}

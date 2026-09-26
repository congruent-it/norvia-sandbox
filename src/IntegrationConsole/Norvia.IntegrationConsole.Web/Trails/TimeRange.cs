namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>A time range the operator can search in, ending now, as it appears in the page's address.</summary>
public sealed record TimeRange(string Code, string Label, TimeSpan Within)
{
    public static readonly TimeRange Default = new("24h", "Last 24 hours", TimeSpan.FromHours(24));

    /// <summary>
    /// The ranges on offer. The last reaches as far back as the estate keeps anything; the application caps every range
    /// at that retention period anyway.
    /// </summary>
    public static IReadOnlyList<TimeRange> All(int retentionDays) =>
    [
        new("1h", "Last hour", TimeSpan.FromHours(1)),
        Default,
        new("7d", "Last 7 days", TimeSpan.FromDays(7)),
        new("30d", "Last 30 days", TimeSpan.FromDays(30)),
        new("all", $"Everything kept ({retentionDays} days)", TimeSpan.FromDays(retentionDays)),
    ];

    /// <summary>The range with this code, or the default for none or one the page doesn't know.</summary>
    public static TimeRange Of(string? code, int retentionDays) =>
        All(retentionDays).FirstOrDefault(range => range.Code == code) ?? Default;
}

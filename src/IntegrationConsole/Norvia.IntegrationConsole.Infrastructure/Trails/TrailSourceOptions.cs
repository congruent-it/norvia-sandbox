namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>Where the estate's telemetry is: the Log Analytics workspace behind its Application Insights.</summary>
public sealed class TrailSourceOptions
{
    public const string SectionName = "Estate";

    /// <summary>
    /// The workspace's ID (its customer ID, a GUID), which the estate Bicep outputs. Empty locally until a developer
    /// points the console at a deployed estate; search then says it isn't connected.
    /// </summary>
    public string? LogAnalyticsWorkspaceId { get; set; }
}

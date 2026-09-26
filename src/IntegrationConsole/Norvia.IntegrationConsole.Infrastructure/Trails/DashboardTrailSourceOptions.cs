namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// Where the Aspire dashboard is when the console runs under the local AppHost without a deployed estate (ADR 0027).
/// The AppHost sets it only then, so its presence is what makes the console search the dashboard.
/// </summary>
public sealed class DashboardTrailSourceOptions
{
    public const string SectionName = "LocalTelemetry";

    /// <summary>The dashboard's own address, whose telemetry API is under <c>/api/telemetry</c>.</summary>
    public Uri? DashboardUrl { get; set; }

    /// <summary>The telemetry API's key, the AppHost's <c>AppHost:DashboardApiKey</c>; empty when the API is unsecured.</summary>
    public string? ApiKey { get; set; }
}

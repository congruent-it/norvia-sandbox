namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>
/// Where the estate's telemetry is searched: the Log Analytics workspace behind Application Insights (ADR 0017), or
/// locally the Aspire dashboard (ADR 0027). It reads the tracking contract's keys only (coding guidelines §7), never a
/// message body.
/// </summary>
public interface ITrailSource
{
    /// <summary>
    /// The Message trails that carried the key, as a correlation ID or a business key, between <see cref="TrailSearch.From"/>
    /// and <see cref="TrailSearch.To"/>, in no particular order.
    /// </summary>
    Task<IReadOnlyList<FoundTrail>> SearchAsync(TrailSearch search, CancellationToken cancellationToken);
}

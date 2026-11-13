namespace Norvia.IntegrationConsole.Application.Refusals;

/// <summary>
/// Where the calls API Management refused at the edge are listed (401, 403 and 429), before any Logic App ran. In Azure
/// that is Application Insights: the policies' traces with outcome <c>Refused</c>, and the gateway's own request telemetry
/// for the 401s no policy sees (a missing or wrong subscription key). Locally it is the telemetry the Aspire dashboard
/// holds from the stand-in (ADR 0023, ADR 0027). It reads the tracking contract's keys only, never a body, a token or a
/// subscription key.
/// </summary>
public interface IRefusedCalls
{
    /// <summary>The calls refused since <paramref name="since"/>, in no particular order.</summary>
    Task<IReadOnlyList<RecordedRefusal>> RefusalsAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

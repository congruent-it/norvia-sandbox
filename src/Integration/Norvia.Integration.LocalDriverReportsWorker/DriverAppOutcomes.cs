using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>
/// Tells the Driver app what became of a report, in the Canonical Model's <c>driver-report-outcome</c> shape, as the Driver reports Logic App does: the
/// action's correlation ID and a token of the identity holding the outcome role. It doesn't retry: the flow does, under the workflow's policy.
/// </summary>
internal sealed class DriverAppOutcomes(HttpClient http, IOptions<DriverReportsWorkerOptions> options, TimeProvider time)
{
    private const string OutcomeRole = "DriverReports.ReportOutcome";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The Driver app's HTTP status, or 0 when it didn't answer.</summary>
    public async Task<int> ReportAsync(DriverReport report, OutcomeReport outcome, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/report-outcomes", UriKind.Relative))
            {
                Content = JsonContent.Create(outcome, options: Json),
            };
            request.Headers.Add("X-Correlation-ID", report.CorrelationId);
            var tokens = options.Value.DriverApp.Tokens;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TmsTokens.Create(tokens, time, tokens.ClientId!, OutcomeRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return (int)response.StatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
    }
}

internal enum ReportOutcome
{
    Recorded,
    Refused,
    NotRecorded,
}

/// <summary>The body of the Canonical Model's <c>driver-report-outcome</c>.</summary>
internal sealed record OutcomeReport(string DriverAppEventId, string TripNumber, string CorrelationId, ReportOutcome Outcome, string? FailureCode, string? Message)
{
    public static OutcomeReport For(DriverReport report, ReportOutcome outcome, string? failureCode = null, string? message = null) =>
        new(report.EventId, report.TripNumber, report.CorrelationId, outcome, failureCode, message);
}

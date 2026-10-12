using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>
/// Calls the TMS Trip API for a Driver's report as the Driver reports Logic App does: the Driver ID from the event, which the Driver app took from the
/// Driver's token, the event's ID so a retry is recognised, the action's correlation ID and a token of the identity holding the role. It doesn't retry:
/// the flow does, under the workflow's policy.
/// </summary>
internal sealed class TmsTripCalls(HttpClient http, IOptions<DriverReportsWorkerOptions> options, TimeProvider time)
{
    private const string StartRole = "Trips.Start";

    public async Task<TmsAnswer> StartAsync(DriverReport report, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{Uri.EscapeDataString(report.TripNumber)}/start", UriKind.Relative))
            {
                Content = JsonContent.Create(new { driverId = report.DriverId, driverAppEventId = report.EventId }),
            };
            request.Headers.Add("X-Correlation-ID", report.CorrelationId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TmsTokens.Create(options.Value.TmsTokens, time, options.Value.TmsTokens.ClientId!, StartRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return TmsAnswer.Of((int)response.StatusCode, response.ReasonPhrase, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // A timeout is a canceled request the caller didn't cancel.
            return new(0, null, exception is TaskCanceledException ? "The TMS timed out." : exception.Message);
        }
    }
}

/// <param name="StatusCode">The TMS's HTTP status, or 0 when it didn't answer.</param>
/// <param name="Code">The failure code of a refusal, as the TMS's problem names it.</param>
/// <param name="Message">The refusal's readable message, or what the TMS said, or the technical cause.</param>
internal sealed record TmsAnswer(int StatusCode, string? Code, string Message)
{
    public bool Succeeded => HttpStatus.IsSuccess(StatusCode);

    /// <summary>A server error, a timeout, 429 or no answer, as the workflow's policy retries; a client error is the TMS refusing the report.</summary>
    public bool IsTransient => HttpStatus.IsTransient(StatusCode);

    public static TmsAnswer Of(int statusCode, string? reasonPhrase, string body)
    {
        string? code = null;
        var message = string.IsNullOrWhiteSpace(body) ? reasonPhrase ?? string.Empty : body;
        if (!HttpStatus.IsSuccess(statusCode) && !HttpStatus.IsTransient(statusCode) && TryParse(body) is { } problem)
        {
            code = Text(problem, "code");
            message = Text(problem, "detail") ?? Text(problem, "title") ?? "The TMS refused the report.";
        }

        return new(statusCode, code, message);
    }

    private static JsonObject? TryParse(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>
/// Calls the TMS for a Driver's report as the Driver reports Logic App does: the Trip API's start operation for a Start Trip, the Tracking event API's record
/// operation for a Stop report. The Driver ID comes from the event, which the Driver app took from the Driver's token; the event's ID lets the TMS recognise a
/// retry; the action's correlation ID and a token of the identity holding the role go with it. It doesn't retry: the flow does, under the workflow's policy.
/// </summary>
internal sealed class TmsTripCalls(HttpClient http, IOptions<DriverReportsWorkerOptions> options, TimeProvider time)
{
    private const string StartRole = "Trips.Start";
    private const string RecordTrackingEventRole = "TrackingEvents.Record";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Asks the TMS to do what the report says: start the Trip, or record the Stop report as a Tracking event of Own fleet.</summary>
    public Task<TmsAnswer> CallAsync(DriverReport report, CancellationToken cancellationToken) =>
        report.Stop is { } stop ? SendAsync(report, "/v1/tracking-events", RecordTrackingEventRole, TrackingEventOf(report, stop), cancellationToken) : StartAsync(report, cancellationToken);

    private Task<TmsAnswer> StartAsync(DriverReport report, CancellationToken cancellationToken) =>
        SendAsync(
            report, $"/v1/trips/{Uri.EscapeDataString(report.TripNumber)}/start", StartRole, new { driverId = report.DriverId, driverAppEventId = report.EventId }, cancellationToken);

    /// <summary>The Canonical Model's Tracking event of Own fleet: the Driver ID is the source, and the Trip, the Shipment and the version replace a Carrier booking.</summary>
    private static object TrackingEventOf(DriverReport report, StopReport stop) => new
    {
        eventId = report.EventId,
        source = new { type = "OwnFleet", driverId = report.DriverId },
        tripNumber = report.TripNumber,
        shipmentNumber = stop.ShipmentNumber,
        tripVersion = stop.TripVersion,
        type = stop.TrackingEventType,
        eventTime = stop.OccurredAt,
        location = new { place = stop.Place, postcode = stop.Postcode, countryCode = stop.CountryCode },
        remark = stop.Remark,
    };

    private async Task<TmsAnswer> SendAsync(DriverReport report, string path, string role, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
            {
                Content = JsonContent.Create(body, options: Json),
            };
            request.Headers.Add("X-Correlation-ID", report.CorrelationId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TmsTokens.Create(options.Value.TmsTokens, time, options.Value.TmsTokens.ClientId!, role));

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
            code = JsonFields.Text(problem, "code");
            message = JsonFields.Text(problem, "detail") ?? JsonFields.Text(problem, "title") ?? "The TMS refused the report.";
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
}

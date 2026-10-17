using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Worlds;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.DriverApi;

/// <summary>
/// Works a Trip through the Driver app API as the Driver, each call with the Driver's own token, as the Driver's screens would. Each report goes in a span
/// named like the Driver app's action (<c>Start Trip</c>, <c>Collected</c>, <c>Deliver</c>, ...), which is the Simulator's hop on the Message trail; once the answer
/// is in it is logged with the tracking contract's keys the Simulator knows (coding guidelines §7). The Driver app takes a report at once and the TMS records it
/// later, so an answer is that the report was taken, not that it was recorded.
/// </summary>
internal sealed partial class NorviaDriverAppClient(HttpClient http, IDriverTokens tokens, SimulatorActivities activities, ILogger<NorviaDriverAppClient> logger) : IDriverApp
{
    private const string FlowName = "trip-execution";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<DriverTripSnapshot>?> GetTripsAsync(SimulatedDriver driver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(driver);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/driver/trips", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(driver.Id, cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogTripsUnavailable(driver.Id, (int)response.StatusCode);
                return null;
            }

            return DriverAnswers.Trips(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTripsNotRead(exception, driver.Id);
            return null;
        }
    }

    public Task<DriverCallAnswer> StartTripAsync(SimulatedDriver driver, string tripNumber, CancellationToken cancellationToken) =>
        SendAsync(driver, DriverActions.StartTrip, tripNumber, shipmentNumber: null, $"/v1/driver/trips/{Escape(tripNumber)}/start", body: null, cancellationToken);

    public Task<DriverCallAnswer> CollectAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, CancellationToken cancellationToken) =>
        SendAsync(driver, DriverActions.Collected, tripNumber, shipmentNumber, StopPath(tripNumber, shipmentNumber, "collection/collected"), new StopReport(null), cancellationToken);

    public Task<DriverCallAnswer> FailCollectionAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, string remark, CancellationToken cancellationToken) =>
        SendAsync(driver, DriverActions.CollectionFailed, tripNumber, shipmentNumber, StopPath(tripNumber, shipmentNumber, "collection/collection-failed"), new StopReport(remark), cancellationToken);

    public Task<DriverCallAnswer> FailDeliveryAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, string remark, CancellationToken cancellationToken) =>
        SendAsync(driver, DriverActions.DeliveryFailed, tripNumber, shipmentNumber, StopPath(tripNumber, shipmentNumber, "delivery/delivery-failed"), new StopReport(remark), cancellationToken);

    public Task<DriverCallAnswer> ReportDamageAsync(
        SimulatedDriver driver, string tripNumber, string shipmentNumber, bool atDelivery, string remark, CancellationToken cancellationToken) =>
        SendAsync(
            driver, DriverActions.ReportDamage, tripNumber, shipmentNumber, StopPath(tripNumber, shipmentNumber, $"{(atDelivery ? "delivery" : "collection")}/damaged"), new StopReport(remark),
            cancellationToken);

    public Task<DriverCallAnswer> DeliverAsync(
        SimulatedDriver driver, string tripNumber, string shipmentNumber, DriverDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return SendAsync(
            driver, DriverActions.Deliver, tripNumber, shipmentNumber, StopPath(tripNumber, shipmentNumber, "delivery/deliver"),
            new Delivery(delivery.ReceivedBy, delivery.Reservations ? "Reservations" : "Clean", delivery.Remark, new ProofFile("image/png", delivery.Signature.ToArray()), [.. delivery.Photos.Select(photo => new ProofFile("image/png", photo.ToArray()))]), cancellationToken);
    }

    public Task<DriverCallAnswer> EndTripAsync(SimulatedDriver driver, string tripNumber, CancellationToken cancellationToken) =>
        SendAsync(driver, DriverActions.EndTrip, tripNumber, shipmentNumber: null, $"/v1/driver/trips/{Escape(tripNumber)}/end", body: null, cancellationToken);

    private async Task<DriverCallAnswer> SendAsync(
        SimulatedDriver driver, string action, string tripNumber, string? shipmentNumber, string path, object? body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentException.ThrowIfNullOrWhiteSpace(tripNumber);

        using var span = activities.Source.StartActivity(action, ActivityKind.Internal);
        DriverCallAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(driver.Id, cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            answer = DriverAnswers.Reported((int)response.StatusCode, responseBody, response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached the Driver app, so nothing was reported.
            LogNotSent(exception, action);
            answer = DriverAnswers.NotReceived("The call didn't reach the Driver app API.");
        }

        using (logger.BeginScope(TrackingKeysOf(answer, driver, tripNumber, shipmentNumber)))
        {
            LogAnswered(action, answer.Outcome);
        }

        return answer;
    }

    /// <summary>
    /// The tracking contract's keys the Simulator knows (coding guidelines §7), so its trace joins the Driver's Message trail. A key that doesn't apply is left
    /// out, never sent empty.
    /// </summary>
    internal static Dictionary<string, object?> TrackingKeysOf(DriverCallAnswer answer, SimulatedDriver driver, string tripNumber, string? shipmentNumber)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(driver);

        var keys = new Dictionary<string, object?> { ["flow"] = FlowName, ["tripNumber"] = tripNumber, ["partyId"] = driver.Id };
        switch (answer.Outcome)
        {
            case LogOutcome.DriverReported:
                keys["outcome"] = "Succeeded";
                break;
            case LogOutcome.DriverRefused:
                keys["outcome"] = "Refused";
                keys["failureCode"] = answer.Detail?.Split(' ')[0];
                break;
            default:
                keys["outcome"] = "Failed";
                keys["failureCode"] = "DriverReportNotReceived";
                break;
        }

        if (!string.IsNullOrWhiteSpace(answer.CorrelationId))
        {
            keys["correlationId"] = answer.CorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(shipmentNumber))
        {
            keys["shipmentNumber"] = shipmentNumber;
        }

        return keys;
    }

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static string StopPath(string tripNumber, string shipmentNumber, string stopAndAction) =>
        $"/v1/driver/trips/{Escape(tripNumber)}/stops/{Escape(shipmentNumber)}/{stopAndAction}";

    // What the Simulator asks the Driver app API to report at a Stop.
    private sealed record StopReport(string? Remark);

    private sealed record ProofFile(string ContentType, byte[] Content);

    private sealed record Delivery(string ReceivedBy, string Condition, string? Remark, ProofFile Signature, IReadOnlyList<ProofFile> Photos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Action} didn't reach the Driver app API")]
    private partial void LogNotSent(Exception exception, string action);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver app answered {Action}: {Outcome}")]
    private partial void LogAnswered(string action, LogOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Driver app API answered the Trips of {DriverId} with {StatusCode}")]
    private partial void LogTripsUnavailable(string driverId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Trips of {DriverId} couldn't be read from the Driver app API")]
    private partial void LogTripsNotRead(Exception exception, string driverId);
}

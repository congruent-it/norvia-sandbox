using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.TmsApi;

/// <summary>
/// Plans through the TMS API as the Simulator's own identity, as the TMS UI does for the Planner. Each assignment and
/// reassignment goes in a span of its own, named like the TMS UI's (<c>Assign Transport Order</c>, <c>Reassign Shipment</c>), which
/// is the Simulator's hop on the Message trail; once the answer is in it is logged with the tracking contract's keys the
/// Simulator knows (coding guidelines §7). Each goes under an idempotency key of its own that every retry of it repeats.
/// </summary>
internal sealed partial class NorviaTmsApiClient(
    HttpClient http,
    ITmsTokens tokens,
    SimulatorActivities activities,
    ILogger<NorviaTmsApiClient> logger)
    : ITmsApi
{
    private const string FlowName = "transport-planning";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<FleetDay>?> GetOwnFleetDaysAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/capacity/days", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogCapacityUnavailable((int)response.StatusCode);
                return null;
            }

            return PlannerAnswers.Days(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCapacityNotRead(exception);
            return null;
        }
    }

    public Task<PlannerAnswer> AssignAsync(string transportOrderNumber, Assignee assignee, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        return SendAsync(
            "Assign Transport Order", $"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/assign", assignee, idempotencyKey,
            (status, body, correlation) => PlannerAnswers.Assigned(status, body, correlation), transportOrderNumber, shipmentNumber: null, cancellationToken);
    }

    public Task<PlannerAnswer> ReassignAsync(string shipmentNumber, Assignee assignee, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentNumber);
        return SendAsync(
            "Reassign Shipment", $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/reassign", assignee, idempotencyKey,
            (status, body, correlation) => PlannerAnswers.Reassigned(status, body, correlation, shipmentNumber), transportOrderNumber: null, shipmentNumber, cancellationToken);
    }

    private async Task<PlannerAnswer> SendAsync(
        string spanName, string path, Assignee assignee, string idempotencyKey, Func<int, string, string?, PlannerAnswer> read,
        string? transportOrderNumber, string? shipmentNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignee);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        using var span = activities.Source.StartActivity(spanName, ActivityKind.Internal);
        PlannerAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
            {
                Content = JsonContent.Create(new Assignment(assignee.IsOwnFleet ? AssignedTo.OwnFleet : AssignedTo.Carrier, assignee.CarrierId), options: Json),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            request.Headers.Add("Idempotency-Key", idempotencyKey);

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            answer = read((int)response.StatusCode, body, response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached the TMS, so nothing was planned.
            LogNotSent(exception, spanName);
            answer = PlannerAnswers.NotReceived("The call didn't reach the TMS API.");
        }

        using (logger.BeginScope(TrackingKeysOf(answer, transportOrderNumber, shipmentNumber)))
        {
            LogAnswered(spanName, answer.Outcome);
        }

        return answer;
    }

    /// <summary>
    /// The tracking contract's keys the Simulator knows (coding guidelines §7), so its trace joins the TMS's Message trail.
    /// A key that doesn't apply is left out, never sent empty.
    /// </summary>
    internal static Dictionary<string, object?> TrackingKeysOf(PlannerAnswer answer, string? transportOrderNumber, string? shipmentNumber)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var keys = new Dictionary<string, object?> { ["flow"] = FlowName };
        switch (answer.Outcome)
        {
            case LogOutcome.Planned or LogOutcome.Reassigned:
                keys["outcome"] = "Succeeded";
                break;
            case LogOutcome.PlanningRefused:
                keys["outcome"] = "Refused";
                keys["failureCode"] = answer.FailureCode;
                break;
            default:
                keys["outcome"] = "Failed";
                keys["failureCode"] = "PlanningNotReceived";
                break;
        }

        foreach (var (key, value) in new[]
        {
            ("correlationId", answer.CorrelationId),
            ("transportOrderNumber", transportOrderNumber),
            ("shipmentNumber", answer.ShipmentNumber ?? shipmentNumber),
        })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys[key] = value;
            }
        }

        return keys;
    }

    private enum AssignedTo
    {
        OwnFleet,
        Carrier,
    }

    // What the Simulator asks the TMS API to assign or reassign to.
    private sealed record Assignment(AssignedTo AssignedTo, string? CarrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Action} didn't reach the TMS API")]
    private partial void LogNotSent(Exception exception, string action);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS answered {Action}: {Outcome}")]
    private partial void LogAnswered(string action, LogOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS API answered the Own fleet's open days with {StatusCode}")]
    private partial void LogCapacityUnavailable(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Own fleet's open days couldn't be read from the TMS API")]
    private partial void LogCapacityNotRead(Exception exception);
}

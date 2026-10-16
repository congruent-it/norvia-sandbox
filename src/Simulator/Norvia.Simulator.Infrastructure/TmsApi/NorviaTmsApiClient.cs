using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Trips;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.TmsApi;

/// <summary>
/// Plans through the TMS API as the Simulator's own identity, as the TMS UI does for the Planner. Each assignment, reassignment,
/// release, cancellation of a Carrier booking and resolution of an Exception goes in a span of its own, named like the TMS UI's
/// (<c>Assign Transport Order</c>, <c>Reassign Shipment</c>, <c>Release Shipment</c>, ...), which
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
    private const string ExceptionFlowName = "exception-handling";
    private const string TripFlowName = "trip-dispatch";

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
            PlannerActions.Assign, $"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/assign", AssignmentOf(assignee), idempotencyKey,
            (status, body, correlation) => PlannerAnswers.Assigned(status, body, correlation), FlowName, transportOrderNumber, shipmentNumber: null, cancellationToken);
    }

    public Task<PlannerAnswer> ReassignAsync(string shipmentNumber, Assignee assignee, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentNumber);
        return SendAsync(
            PlannerActions.Reassign, $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/reassign", AssignmentOf(assignee), idempotencyKey,
            (status, body, correlation) => PlannerAnswers.Reassigned(status, body, correlation, shipmentNumber), FlowName, transportOrderNumber: null, shipmentNumber, cancellationToken);
    }

    public Task<PlannerAnswer> ReleaseAsync(string shipmentNumber, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentNumber);
        return SendAsync(
            PlannerActions.Release, $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/release", body: null, idempotencyKey,
            (status, responseBody, correlation) => PlannerAnswers.Released(status, responseBody, correlation), FlowName, transportOrderNumber: null, shipmentNumber, cancellationToken);
    }

    public Task<PlannerAnswer> CreateTripAsync(TripDraft draft, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return SendAsync(
            TripActions.CreateTrip, "/v1/trips",
            new NewTrip(draft.Day, draft.Vehicle.Id, draft.Driver.Id, draft.PlannedStart, draft.PlannedDrivingDuration, [.. draft.Shipments.Select(shipment => shipment.ShipmentNumber)]),
            idempotencyKey, (status, body, correlation) => PlannerAnswers.TripCreated(status, body, correlation), TripFlowName, transportOrderNumber: null, shipmentNumber: null,
            cancellationToken);
    }

    public Task<PlannerAnswer> DispatchTripAsync(string tripNumber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tripNumber);
        return SendAsync(
            TripActions.DispatchTrip, $"/v1/trips/{Uri.EscapeDataString(tripNumber)}/dispatch", body: null, idempotencyKey: null,
            (status, body, correlation) => PlannerAnswers.TripDispatched(status, body, correlation, tripNumber), TripFlowName, transportOrderNumber: null, shipmentNumber: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OpenExceptionSummary>?> GetOpenExceptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/exceptions/open", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogExceptionsUnavailable((int)response.StatusCode);
                return null;
            }

            return PlannerAnswers.OpenExceptions(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogExceptionsNotRead(exception);
            return null;
        }
    }

    public Task<PlannerAnswer> CancelCarrierBookingAsync(string exceptionNumber, string reason, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionNumber);
        return SendAsync(
            PlannerActions.CancelCarrierBooking, $"/v1/exceptions/{Uri.EscapeDataString(exceptionNumber)}/cancel-carrier-booking", new { Reason = reason }, idempotencyKey,
            (status, body, correlation) => PlannerAnswers.Cancelled(status, body, correlation), ExceptionFlowName, transportOrderNumber: null, shipmentNumber: null, cancellationToken);
    }

    public Task<PlannerAnswer> ResolveReplannedAsync(string exceptionNumber, string remark, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionNumber);
        return SendAsync(
            PlannerActions.ResolveException, $"/v1/exceptions/{Uri.EscapeDataString(exceptionNumber)}/resolve", new { Outcome = "Replanned", Remark = remark }, idempotencyKey: null,
            (status, body, correlation) => PlannerAnswers.Resolved(status, body, correlation), ExceptionFlowName, transportOrderNumber: null, shipmentNumber: null, cancellationToken);
    }

    private static Assignment AssignmentOf(Assignee assignee)
    {
        ArgumentNullException.ThrowIfNull(assignee);
        return new Assignment(assignee.IsOwnFleet ? AssignedTo.OwnFleet : AssignedTo.Carrier, assignee.CarrierId);
    }

    private async Task<PlannerAnswer> SendAsync(
        string spanName, string path, object? body, string? idempotencyKey, Func<int, string, string?, PlannerAnswer> read, string flow,
        string? transportOrderNumber, string? shipmentNumber, CancellationToken cancellationToken)
    {
        if (idempotencyKey is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        }

        using var span = activities.Source.StartActivity(spanName, ActivityKind.Internal);
        PlannerAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            if (idempotencyKey is not null)
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            answer = read((int)response.StatusCode, responseBody, response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached the TMS, so nothing was planned.
            LogNotSent(exception, spanName);
            answer = PlannerAnswers.NotReceived("The call didn't reach the TMS API.");
        }

        using (logger.BeginScope(TrackingKeysOf(answer, transportOrderNumber, shipmentNumber, flow)))
        {
            LogAnswered(spanName, answer.Outcome);
        }

        return answer;
    }

    /// <summary>
    /// The tracking contract's keys the Simulator knows (coding guidelines §7), so its trace joins the TMS's Message trail.
    /// A key that doesn't apply is left out, never sent empty.
    /// </summary>
    internal static Dictionary<string, object?> TrackingKeysOf(PlannerAnswer answer, string? transportOrderNumber, string? shipmentNumber, string flow = FlowName)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var keys = new Dictionary<string, object?> { ["flow"] = flow };
        switch (answer.Outcome)
        {
            case LogOutcome.Planned or LogOutcome.Reassigned or LogOutcome.Released or LogOutcome.BookingCancelled or LogOutcome.ExceptionResolved or LogOutcome.TripBuilt or LogOutcome.TripDispatched:
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
            ("tripNumber", answer.TripNumber),
        })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys[key] = value;
            }
        }

        return keys;
    }

    // What the Simulator asks the TMS API to create a Draft Trip from.
    private sealed record NewTrip(DateOnly Day, string VehicleId, string DriverId, TimeOnly PlannedStart, TimeSpan PlannedDrivingDuration, IReadOnlyList<string> ShipmentNumbers);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS API answered the Open Exceptions with {StatusCode}")]
    private partial void LogExceptionsUnavailable(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Open Exceptions couldn't be read from the TMS API")]
    private partial void LogExceptionsNotRead(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Own fleet's open days couldn't be read from the TMS API")]
    private partial void LogCapacityNotRead(Exception exception);
}

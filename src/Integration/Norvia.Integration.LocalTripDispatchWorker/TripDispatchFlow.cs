using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum TripDispatchFlowOutcome
{
    /// <summary>The Trip version was sent and reported delivered, or reported not delivered.</summary>
    Done,

    /// <summary>The message isn't a <c>TripDispatched</c> the Driver app can be sent, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The TMS didn't answer the report.</summary>
    TmsUnavailable,

    /// <summary>The TMS refused the report, which sending it again won't change.</summary>
    TmsRefused,
}

/// <summary>
/// The P11 Trip dispatch Logic App's steps, played locally: send the Trip, as the event carries it, to the Driver app's Trip
/// endpoint under the bounded exponential retry policy, and report the version delivered or not delivered to the TMS under the
/// action's correlation ID.
/// </summary>
internal sealed partial class TripDispatchFlow(
    DriverAppEndpoint driverApp,
    TmsReports tms,
    IOptions<TripDispatchWorkerOptions> options,
    TimeProvider time,
    ILogger<TripDispatchFlow> logger)
{
    private const string EventType = "TripDispatched";

    public async Task<TripDispatchFlowOutcome> RunAsync(string tripDispatched, CancellationToken cancellationToken)
    {
        var request = Parse(tripDispatched) is { } parsed ? EventOf(parsed) : null;
        if (request is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, TripDispatchFailureCodes.Unreadable));
            LogUnreadable();
            return TripDispatchFlowOutcome.Unreadable;
        }

        using var trail = logger.BeginScope(request.Keys);
        var (answer, attempts) = await SendAsync(request, tripDispatched, cancellationToken);

        var status = answer.Succeeded
            ? await tms.ReportDeliveredAsync(request.TripNumber, request.Version, request.CorrelationId, cancellationToken)
            : await tms.ReportNotDeliveredAsync(request.TripNumber, request.Version, request.CorrelationId, CauseOf(answer, attempts), cancellationToken);

        using var reported = logger.BeginScope(ReportOutcome(answer, status));
        LogReported(answer.Succeeded ? "delivered" : "not delivered", status);
        return status switch
        {
            >= 200 and < 300 => TripDispatchFlowOutcome.Done,
            0 or 408 or 429 or >= 500 => TripDispatchFlowOutcome.TmsUnavailable,
            _ => TripDispatchFlowOutcome.TmsRefused,
        };
    }

    private async Task<(DriverAppAnswer Answer, int Attempts)> SendAsync(TripEvent request, string trip, CancellationToken cancellationToken)
    {
        var endpoint = options.Value.DriverApp;
        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await driverApp.SendAsync(endpoint, trip, request.CorrelationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return (answer, attempt + 1);
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, TripDispatchFailureCodes.DriverAppUnavailable));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    // The technical cause the Planner reads on the version, and the TMS publishes on TripNotDelivered.
    private static string CauseOf(DriverAppAnswer answer, int attempts) =>
        $"{(answer.StatusCode == 0 ? "The Driver app didn't answer" : $"The Driver app answered {answer.StatusCode}")}: {answer.Message} ({attempts} {(attempts == 1 ? "attempt" : "attempts")}).";

    // The outcome of the step that reports to the TMS, as the workflow's tracked properties work it out.
    private static Dictionary<string, object?> ReportOutcome(DriverAppAnswer answer, int tmsStatus)
    {
        if (tmsStatus is >= 200 and < 300)
        {
            return answer.Succeeded
                ? TrackingKeys.OutcomeOf(TrackingKeys.Succeeded)
                : TrackingKeys.OutcomeOf(TrackingKeys.Failed, answer.IsRetryable ? TripDispatchFailureCodes.DriverAppUnavailable : TripDispatchFailureCodes.DriverAppRefused);
        }

        return TrackingKeys.OutcomeOf(TrackingKeys.Failed, tmsStatus is 0 or 408 or 429 or >= 500 ? TripDispatchFailureCodes.TmsUnavailable : TripDispatchFailureCodes.TmsCallRefused);
    }

    private static JsonObject? Parse(string tripDispatched)
    {
        try
        {
            return JsonNode.Parse(tripDispatched) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static TripEvent? EventOf(JsonObject parsed) =>
        Text(parsed, "eventType") == EventType
        && Text(parsed, "correlationId") is { } correlationId
        && Text(parsed, "tripNumber") is { } tripNumber
        && parsed["version"] is JsonValue version && version.TryGetValue<int>(out var number) && number > 0
        && parsed["driver"] is JsonObject driver && Text(driver, "driverId") is { } driverId
            ? new(correlationId, tripNumber, number, driverId, Text(parsed, "eventId"))
            : null;

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a Trip version the Driver app can be sent")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Driver app answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported the Trip version {Report} to the TMS, which answered {StatusCode}")]
    private partial void LogReported(string report, int statusCode);

    /// <summary>What the event names, and the tracking contract's keys it gives the trail.</summary>
    private sealed record TripEvent(string CorrelationId, string TripNumber, int Version, string DriverId, string? EventId)
    {
        public Dictionary<string, object?> Keys
        {
            get
            {
                var keys = new Dictionary<string, object?>
                {
                    [TrackingKeys.TripNumber] = TripNumber,
                    [TrackingKeys.CorrelationId] = CorrelationId,
                    [TrackingKeys.Flow] = TrackingKeys.TripDispatch,
                    [TrackingKeys.PartyId] = DriverId,
                };
                if (EventId is not null)
                {
                    keys[TrackingKeys.SourceMessageId] = EventId;
                }

                return keys;
            }
        }
    }
}

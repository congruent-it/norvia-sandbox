using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierCancellationWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum CarrierCancellationFlowOutcome
{
    /// <summary>The cancellation was sent and reported told, or reported not delivered.</summary>
    Done,

    /// <summary>The message isn't a <c>CarrierBookingCancelled</c> or <c>CarrierBookingWithdrawn</c> the Carrier can be told of, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The TMS didn't answer the report.</summary>
    TmsUnavailable,

    /// <summary>The TMS refused the report, which sending it again won't change.</summary>
    TmsRefused,
}

/// <summary>
/// The P7 Carrier cancellation Logic App's steps, played locally (ADR 0034): map the cancellation, send it to the Carrier's
/// endpoint under the bounded exponential retry policy, and report the outcome to the TMS under the action's correlation ID.
/// </summary>
internal sealed partial class CarrierCancellationFlow(
    CarrierEndpoint carrier,
    TmsReports tms,
    IOptions<CarrierCancellationWorkerOptions> options,
    TimeProvider time,
    ILogger<CarrierCancellationFlow> logger)
{
    public async Task<CarrierCancellationFlowOutcome> RunAsync(string carrierCancellationEvent, CancellationToken cancellationToken)
    {
        var parsed = Parse(carrierCancellationEvent);
        var request = parsed is null ? null : EventOf(parsed);
        var cancellation = parsed is null ? null : CarrierCancellationMap.ToCarrierCancellation(parsed);
        if (request is null || cancellation is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, CarrierCancellationFailureCodes.Unreadable));
            LogUnreadable();
            return CarrierCancellationFlowOutcome.Unreadable;
        }

        using var trail = logger.BeginScope(request.Keys);
        var answer = await SendAsync(request, cancellation.ToJsonString(), cancellationToken);

        var status = answer.Succeeded
            ? await tms.ReportToldAsync(request.CarrierBookingNumber, request.CorrelationId, cancellationToken)
            : await tms.ReportNotDeliveredAsync(request.CarrierBookingNumber, request.CorrelationId, answer.StatusCode, answer.Message, cancellationToken);

        using var reported = logger.BeginScope(ReportOutcome(answer, status));
        LogReported(answer.Succeeded ? "told" : "not delivered", status);
        return status switch
        {
            >= 200 and < 300 => CarrierCancellationFlowOutcome.Done,
            0 or 408 or 429 or >= 500 => CarrierCancellationFlowOutcome.TmsUnavailable,
            _ => CarrierCancellationFlowOutcome.TmsRefused,
        };
    }

    private async Task<CarrierAnswer> SendAsync(CancellationEvent request, string cancellation, CancellationToken cancellationToken)
    {
        if (!options.Value.Carriers.TryGetValue(request.CarrierId, out var endpoint))
        {
            return new(0, $"No cancellation endpoint is configured for Carrier {request.CarrierId}.");
        }

        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await carrier.SendAsync(endpoint, cancellation, request.CorrelationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, CarrierCancellationFailureCodes.CarrierUnavailable));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    // The outcome of the step that reports to the TMS, as the workflow's tracked properties work it out.
    private static Dictionary<string, object?> ReportOutcome(CarrierAnswer answer, int tmsStatus)
    {
        if (tmsStatus is >= 200 and < 300)
        {
            return answer.Succeeded
                ? TrackingKeys.OutcomeOf(TrackingKeys.Succeeded)
                : TrackingKeys.OutcomeOf(TrackingKeys.Failed, answer.IsRetryable ? CarrierCancellationFailureCodes.CarrierUnavailable : CarrierCancellationFailureCodes.CarrierRefused);
        }

        return TrackingKeys.OutcomeOf(TrackingKeys.Failed, tmsStatus is 0 or 408 or 429 or >= 500 ? CarrierCancellationFailureCodes.TmsUnavailable : CarrierCancellationFailureCodes.TmsCallRefused);
    }

    private static JsonObject? Parse(string carrierCancellationEvent)
    {
        try
        {
            return JsonNode.Parse(carrierCancellationEvent) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static CancellationEvent? EventOf(JsonObject parsed) =>
        Text(parsed, "correlationId") is { } correlationId
        && Text(parsed, "carrierBookingNumber") is { } carrierBookingNumber
        && Text(parsed, "carrierId") is { } carrierId
            ? new(correlationId, carrierBookingNumber, carrierId, Text(parsed, "shipmentNumber"), Text(parsed, "eventId"))
            : null;

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a Carrier cancellation request")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Carrier's endpoint answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported the cancellation {Report} to the TMS, which answered {StatusCode}")]
    private partial void LogReported(string report, int statusCode);

    /// <summary>What the event names, and the tracking contract's keys it gives the trail.</summary>
    private sealed record CancellationEvent(string CorrelationId, string CarrierBookingNumber, string CarrierId, string? ShipmentNumber, string? EventId)
    {
        public Dictionary<string, object?> Keys
        {
            get
            {
                var keys = new Dictionary<string, object?>
                {
                    [TrackingKeys.CarrierBookingNumber] = CarrierBookingNumber,
                    [TrackingKeys.CorrelationId] = CorrelationId,
                    [TrackingKeys.Flow] = TrackingKeys.CarrierCancellation,
                    [TrackingKeys.PartyId] = CarrierId,
                };
                if (ShipmentNumber is not null)
                {
                    keys[TrackingKeys.ShipmentNumber] = ShipmentNumber;
                }

                if (EventId is not null)
                {
                    keys[TrackingKeys.SourceMessageId] = EventId;
                }

                return keys;
            }
        }
    }
}

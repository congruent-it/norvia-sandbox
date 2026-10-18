using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalBillingWorker;

/// <summary>
/// How the flow's run ended: the message is completed, or dead-lettered with <see cref="Reason"/> as <c>DeadLetterReason</c> and
/// <see cref="Description"/> as <c>DeadLetterErrorDescription</c>, as the workflow does.
/// </summary>
internal sealed record BillingRun(string? Reason = null, string? Description = null)
{
    public bool Recorded => Reason is null;
}

/// <summary>
/// The E1 Billing Logic App's steps, played locally: record the Billable line with the ERP billing API, sending the event as it is,
/// under the bounded exponential retry policy and the event's correlation ID.
/// </summary>
internal sealed partial class BillingFlow(ErpBillingApi erp, IOptions<BillingWorkerOptions> options, TimeProvider time, ILogger<BillingFlow> logger)
{
    private const string EventType = "TransportOrderBillable";

    public async Task<BillingRun> RunAsync(string transportOrderBillable, CancellationToken cancellationToken)
    {
        var keys = TryReadKeys(transportOrderBillable);
        if (keys is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, BillingFailureCodes.Unreadable));
            LogUnreadable();
            return new(BillingFailureCodes.Unreadable, "The message isn't a TransportOrderBillable the ERP can record, so nothing was sent to the ERP.");
        }

        using var trail = logger.BeginScope(keys);
        var answer = await SendAsync(transportOrderBillable, (string)keys[TrackingKeys.CorrelationId]!, cancellationToken);

        var failureCode = answer.FailureCode;
        using var outcome = logger.BeginScope(TrackingKeys.OutcomeOf(failureCode is null ? TrackingKeys.Succeeded : TrackingKeys.DeadLettered, failureCode));
        LogAnswered(answer.StatusCode);
        return answer.Succeeded ? new() : new(failureCode, answer.Description);
    }

    private async Task<ErpAnswer> SendAsync(string body, string correlationId, CancellationToken cancellationToken)
    {
        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await erp.RecordAsync(body, correlationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, BillingFailureCodes.ErpUnavailable));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    /// <summary>The tracking contract's keys the event carries, or null when it isn't a <c>TransportOrderBillable</c> with the ones the flow needs.</summary>
    private static Dictionary<string, object?>? TryReadKeys(string message)
    {
        JsonObject? parsed;
        try
        {
            parsed = JsonNode.Parse(message) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (parsed is null
            || Text(parsed, "eventType") != EventType
            || Text(parsed, "correlationId") is not { } correlationId
            || Text(parsed, "transportOrderNumber") is not { } transportOrderNumber
            || Text(parsed, "shipperId") is not { } shipperId)
        {
            return null;
        }

        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = TrackingKeys.Billing,
            [TrackingKeys.PartyId] = shipperId,
            [TrackingKeys.TransportOrderNumber] = transportOrderNumber,
        };
        foreach (var (key, field) in new[] { (TrackingKeys.ShipmentNumber, "shipmentNumber"), (TrackingKeys.SourceMessageId, "eventId") })
        {
            if (Text(parsed, field) is { } value)
            {
                keys[key] = value;
            }
        }

        return keys;
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a TransportOrderBillable the ERP can record")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ERP billing API answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "The ERP billing API answered {StatusCode}")]
    private partial void LogAnswered(int statusCode);
}

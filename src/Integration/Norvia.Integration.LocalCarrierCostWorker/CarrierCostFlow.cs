using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierCostWorker;

/// <summary>
/// How the flow's run ended: the message is completed, or dead-lettered with <see cref="Reason"/> as <c>DeadLetterReason</c> and
/// <see cref="Description"/> as <c>DeadLetterErrorDescription</c>, as the workflow does.
/// </summary>
internal sealed record CarrierCostRun(string? Reason = null, string? Description = null)
{
    public bool Recorded => Reason is null;
}

/// <summary>
/// The P9 Carrier cost Logic App's steps, played locally: record the Carrier cost with the ERP billing API, sending the event as it is,
/// under the bounded exponential retry policy and the event's correlation ID.
/// </summary>
internal sealed partial class CarrierCostFlow(ErpCarrierCostApi erp, IOptions<CarrierCostWorkerOptions> options, TimeProvider time, ILogger<CarrierCostFlow> logger)
{
    private const string EventType = "CarrierCostIncurred";

    public async Task<CarrierCostRun> RunAsync(string carrierCostIncurred, CancellationToken cancellationToken, string? messageId = null)
    {
        var keys = TryReadKeys(carrierCostIncurred);
        if (keys is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, CarrierCostFailureCodes.Unreadable, messageId));
            LogUnreadable();
            return new(CarrierCostFailureCodes.Unreadable, "The message isn't a CarrierCostIncurred the ERP can record, so nothing was sent to the ERP.");
        }

        using var trail = logger.BeginScope(keys);
        var answer = await SendAsync(carrierCostIncurred, (string)keys[TrackingKeys.CorrelationId]!, cancellationToken);

        var failureCode = answer.FailureCode;
        using var outcome = logger.BeginScope(TrackingKeys.OutcomeOf(failureCode is null ? TrackingKeys.Succeeded : TrackingKeys.DeadLettered, failureCode, messageId));
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
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, CarrierCostFailureCodes.ErpUnavailable));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    /// <summary>The tracking contract's keys the event carries, or null when it isn't a <c>CarrierCostIncurred</c> with the ones the flow needs.</summary>
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
            || Text(parsed, "carrierBookingNumber") is not { } carrierBookingNumber
            || Text(parsed, "carrierId") is not { } carrierId)
        {
            return null;
        }

        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = TrackingKeys.CarrierCost,
            [TrackingKeys.PartyId] = carrierId,
            [TrackingKeys.CarrierBookingNumber] = carrierBookingNumber,
        };
        foreach (var (key, field) in new[] { (TrackingKeys.TransportOrderNumber, "transportOrderNumber"), (TrackingKeys.ShipmentNumber, "shipmentNumber"), (TrackingKeys.SourceMessageId, "eventId") })
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a CarrierCostIncurred the ERP can record")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ERP billing API answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "The ERP billing API answered {StatusCode}")]
    private partial void LogAnswered(int statusCode);
}

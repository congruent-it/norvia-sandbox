using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalMasterDataSyncWorker;

/// <summary>
/// How the flow's run ended: the message is completed, or dead-lettered with <see cref="Reason"/> as <c>DeadLetterReason</c> and
/// <see cref="Description"/> as <c>DeadLetterErrorDescription</c>, as the workflow does.
/// </summary>
internal sealed record MasterDataSyncRun(string? Reason = null, string? Description = null)
{
    public bool Replaced => Reason is null;
}

/// <summary>
/// The E3 master-data sync Logic App's steps, played locally: map the ERP's event for one record to the TMS's replace command and call it
/// under the bounded exponential retry policy and the event's correlation ID. The TMS keeps the highest Master-data revision it applied and
/// answers <c>2xx</c> to one that is not higher, so a redelivered or resubmitted event is harmless.
/// </summary>
internal sealed partial class MasterDataSyncFlow(TmsMasterDataApi tms, IOptions<MasterDataSyncWorkerOptions> options, TimeProvider time, ILogger<MasterDataSyncFlow> logger)
{
    public async Task<MasterDataSyncRun> RunAsync(string masterDataEvent, CancellationToken cancellationToken, string? messageId = null)
    {
        var call = TryMap(masterDataEvent);
        if (call is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, MasterDataSyncFailureCodes.Unreadable, messageId));
            LogUnreadable();
            return new(MasterDataSyncFailureCodes.Unreadable, "The message isn't a master-data event the TMS can replace a record from, so nothing was sent to the TMS.");
        }

        using var trail = logger.BeginScope(call.Keys);
        var answer = await SendAsync(call, cancellationToken);

        var failureCode = answer.FailureCode;
        using var outcome = logger.BeginScope(TrackingKeys.OutcomeOf(failureCode is null ? TrackingKeys.Succeeded : TrackingKeys.DeadLettered, failureCode, messageId));
        LogAnswered(answer.StatusCode);
        return answer.Succeeded ? new() : new(failureCode, answer.Description);
    }

    private async Task<TmsAnswer> SendAsync(TmsCall call, CancellationToken cancellationToken)
    {
        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await tms.ReplaceAsync(call.Path, call.Body, call.CorrelationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, MasterDataSyncFailureCodes.TmsUnavailable));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    /// <summary>What to send the TMS for the event, or null when it isn't a master-data event the flow can replicate.</summary>
    private static TmsCall? TryMap(string message)
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

        // The entity type is the event type; the record sits under the type's own name, and the TMS has a collection for each.
        var (recordName, collection) = Text(parsed ?? [], "eventType") switch
        {
            "Shipper" => ("shipper", "shippers"),
            "Carrier" => ("carrier", "carriers"),
            _ => (null, null),
        };
        if (parsed is null || recordName is null || Text(parsed, "correlationId") is not { } correlationId
            || parsed[recordName] is not JsonObject record
            || Text(record, "id") is not { } id
            || Text(record, "name") is not { } name
            || record["active"] is not JsonValue active || !active.TryGetValue<bool>(out var isActive)
            || record["revision"] is not JsonValue revision || !revision.TryGetValue<int>(out var revisionNumber))
        {
            return null;
        }

        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = TrackingKeys.MasterData,
            [TrackingKeys.PartyId] = id,
        };
        if (Text(parsed, "eventId") is { } eventId)
        {
            keys[TrackingKeys.SourceMessageId] = eventId;
        }

        var body = new JsonObject { ["name"] = name, ["active"] = isActive, ["revision"] = revisionNumber }.ToJsonString();
        return new TmsCall($"/v1/{collection}/{Uri.EscapeDataString(id)}", body, correlationId, keys);
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private sealed record TmsCall(string Path, string Body, string CorrelationId, Dictionary<string, object?> Keys);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a master-data event the TMS can replace a record from")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS API answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS API answered {StatusCode}")]
    private partial void LogAnswered(int statusCode);
}

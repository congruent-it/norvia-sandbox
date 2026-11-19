using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalConsigneeNotificationWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum ConsigneeNotificationFlowOutcome
{
    /// <summary>The email was sent, or the Transport Order had nothing to send, which is no error.</summary>
    Done,

    /// <summary>The message isn't a <c>ConsigneeNotificationRequested</c> the Consignee can be told of, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The TMS didn't answer.</summary>
    TmsUnavailable,

    /// <summary>The TMS refused the call, which asking again won't change.</summary>
    TmsRefused,

    /// <summary>The TMS's answer or the address couldn't be used, so nothing was sent.</summary>
    EmailUnusable,

    /// <summary>The mail server was unavailable until the retries were used up, or refused the email.</summary>
    EmailNotSent,
}

/// <summary>
/// The P7 Consignee notification Logic App's steps, played locally (ADR 0033): read the request, ask the TMS for the Consignee's address and
/// tracking code under the bounded exponential retry policy, make the email and send it under the same policy. A Transport Order with
/// nothing to send ends the run as Done. The address and the code are never logged, and a failed run never reaches the Shipper's path: it is
/// a separate subscription.
/// </summary>
internal sealed partial class ConsigneeNotificationFlow(
    TmsConsigneeNotifications tms,
    IConsigneeMailbox mailbox,
    IOptions<ConsigneeNotificationWorkerOptions> options,
    TimeProvider time,
    ILogger<ConsigneeNotificationFlow> logger)
{
    public async Task<ConsigneeNotificationFlowOutcome> RunAsync(string consigneeNotificationRequested, CancellationToken cancellationToken)
    {
        if (ConsigneeNotificationMap.ToNotification(consigneeNotificationRequested) is not { } notification)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "ConsigneeNotificationRequestedUnreadable"));
            LogUnreadable();
            return ConsigneeNotificationFlowOutcome.Unreadable;
        }

        var requested = JsonNode.Parse(consigneeNotificationRequested)!.AsObject();
        var transportOrderNumber = notification["transportOrderNumber"]!.GetValue<string>();
        var correlationId = Text(requested, "correlationId") ?? Guid.Empty.ToString();
        using var trail = logger.BeginScope(KeysOf(requested, correlationId, transportOrderNumber));

        var answer = await RetryAsync(
            token => tms.GetAsync(transportOrderNumber, correlationId, token), reply => reply.Succeeded || !reply.IsRetryable, "TmsUnavailable", reply => reply.StatusCode, cancellationToken);
        if (answer.NothingToSend)
        {
            using var nothing = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
            LogNothingToSend();
            return ConsigneeNotificationFlowOutcome.Done;
        }

        if (!answer.Succeeded)
        {
            var unavailable = answer.IsRetryable;
            using var failed = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, unavailable ? "TmsUnavailable" : "TmsCallRefused"));
            LogTmsFailed(answer.StatusCode);
            return unavailable ? ConsigneeNotificationFlowOutcome.TmsUnavailable : ConsigneeNotificationFlowOutcome.TmsRefused;
        }

        if (ConsigneeNotificationMap.ToEmail(consigneeNotificationRequested, answer.Body, options.Value.PartnerPortalBaseAddress!.ToString()) is not { } email)
        {
            using var unusable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "ConsigneeEmailUnusable"));
            LogEmailUnusable();
            return ConsigneeNotificationFlowOutcome.EmailUnusable;
        }

        var sent = await RetryAsync(token => mailbox.SendAsync(email, token), reply => reply.Succeeded || !reply.IsRetryable, "EmailServiceUnavailable", _ => 0, cancellationToken);
        if (!sent.Succeeded)
        {
            using var notSent = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, sent.IsRetryable ? "EmailServiceUnavailable" : "EmailRefused"));
            LogEmailNotSent();
            return ConsigneeNotificationFlowOutcome.EmailNotSent;
        }

        using var done = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
        LogEmailSent();
        return ConsigneeNotificationFlowOutcome.Done;
    }

    private async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> call, Func<T, bool> isFinal, string failureCode, Func<T, int> statusOf, CancellationToken cancellationToken)
    {
        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await call(cancellationToken);
            if (isFinal(answer) || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, failureCode));
            LogRetrying(statusOf(answer), attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    // The keys of the tracking contract the workflow tracks: never the address, the code or a party.
    private static Dictionary<string, object?> KeysOf(JsonObject requested, string correlationId, string transportOrderNumber)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = TrackingKeys.ConsigneeNotification,
            [TrackingKeys.TransportOrderNumber] = transportOrderNumber,
        };
        if (Text(requested, "shipmentNumber") is { } shipmentNumber)
        {
            keys[TrackingKeys.ShipmentNumber] = shipmentNumber;
        }

        if (Text(requested, "eventId") is { } eventId)
        {
            keys[TrackingKeys.SourceMessageId] = eventId;
        }

        return keys;
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a Consignee notification request")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Information, Message = "The Transport Order has nothing to send to a Consignee")]
    private partial void LogNothingToSend();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The call to the dependency failed with status {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Error, Message = "The TMS answered {StatusCode} when asked where to send the Consignee notification")]
    private partial void LogTmsFailed(int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Consignee's address or the tracking link can't be used, so nothing was sent")]
    private partial void LogEmailUnusable();

    [LoggerMessage(Level = LogLevel.Error, Message = "The Consignee notification email was not sent")]
    private partial void LogEmailNotSent();

    [LoggerMessage(Level = LogLevel.Information, Message = "The Consignee notification email was sent")]
    private partial void LogEmailSent();
}

using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalShipperNotificationWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum ShipperNotificationFlowOutcome
{
    /// <summary>The notification was sent and reported sent, or reported not delivered.</summary>
    Done,

    /// <summary>The message isn't a <c>ShipperNotificationRequested</c> the Shipper can be told of, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The TMS didn't answer the report.</summary>
    TmsUnavailable,

    /// <summary>The TMS refused the report, which sending it again won't change.</summary>
    TmsRefused,
}

/// <summary>
/// The P7 Shipper notification Logic App's steps, played locally (ADR 0033): map the notification, send it to the Shipper's
/// endpoint under the bounded exponential retry policy, and report the outcome to the TMS under the action's correlation ID.
/// </summary>
internal sealed partial class ShipperNotificationFlow(
    ShipperEndpoint shipper,
    TmsReports tms,
    IOptions<ShipperNotificationWorkerOptions> options,
    TimeProvider time,
    ILogger<ShipperNotificationFlow> logger)
{
    public async Task<ShipperNotificationFlowOutcome> RunAsync(string shipperNotificationRequested, CancellationToken cancellationToken)
    {
        var request = RequestOf(shipperNotificationRequested);
        var notification = ShipperNotificationMap.ToShipperNotification(shipperNotificationRequested);
        if (request is null || notification is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "ShipperNotificationRequestedUnreadable"));
            LogUnreadable();
            return ShipperNotificationFlowOutcome.Unreadable;
        }

        using var trail = logger.BeginScope(request.Keys);
        var answer = await SendAsync(request, notification.ToJsonString(), cancellationToken);

        var status = answer.Succeeded
            ? await tms.ReportSentAsync(request.NotificationId, request.CorrelationId, cancellationToken)
            : await tms.ReportNotDeliveredAsync(request.NotificationId, request.CorrelationId, answer.StatusCode, answer.Message, cancellationToken);

        using var reported = logger.BeginScope(ReportOutcome(answer, status));
        LogReported(answer.Succeeded ? "sent" : "not delivered", status);
        return status switch
        {
            >= 200 and < 300 => ShipperNotificationFlowOutcome.Done,
            0 or >= 500 => ShipperNotificationFlowOutcome.TmsUnavailable,
            _ => ShipperNotificationFlowOutcome.TmsRefused,
        };
    }

    private async Task<ShipperAnswer> SendAsync(Request request, string notification, CancellationToken cancellationToken)
    {
        if (!options.Value.Shippers.TryGetValue(request.ShipperId, out var endpoint))
        {
            return new(0, $"No notification endpoint is configured for Shipper {request.ShipperId}.");
        }

        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await shipper.SendAsync(endpoint, notification, request.CorrelationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, "ShipperUnavailable"));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    // The outcome of the step that reports to the TMS, as the workflow's tracked properties work it out.
    private static Dictionary<string, object?> ReportOutcome(ShipperAnswer answer, int tmsStatus)
    {
        if (tmsStatus is >= 200 and < 300)
        {
            return answer.Succeeded
                ? TrackingKeys.OutcomeOf(TrackingKeys.Succeeded)
                : TrackingKeys.OutcomeOf(TrackingKeys.Failed, answer.IsRetryable ? "ShipperUnavailable" : "ShipperRefused");
        }

        return TrackingKeys.OutcomeOf(TrackingKeys.Failed, tmsStatus is 0 or >= 500 ? "TmsUnavailable" : "TmsCallRefused");
    }

    private static Request? RequestOf(string shipperNotificationRequested)
    {
        try
        {
            if (JsonNode.Parse(shipperNotificationRequested) is not JsonObject requested
                || Text(requested, "correlationId") is not { } correlationId
                || Text(requested, "notificationId") is not { } notificationId
                || Text(requested, "shipperId") is not { } shipperId)
            {
                return null;
            }

            return new(correlationId, notificationId, shipperId, Text(requested, "transportOrderNumber"), Text(requested, "shipmentNumber"), Text(requested, "eventId"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a Shipper notification request")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Shipper's endpoint answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported the notification {Report} to the TMS, which answered {StatusCode}")]
    private partial void LogReported(string report, int statusCode);

    /// <summary>What a request names, and the tracking contract's keys it gives the trail.</summary>
    private sealed record Request(string CorrelationId, string NotificationId, string ShipperId, string? TransportOrderNumber, string? ShipmentNumber, string? EventId)
    {
        public Dictionary<string, object?> Keys
        {
            get
            {
                var keys = new Dictionary<string, object?>
                {
                    [TrackingKeys.CorrelationId] = CorrelationId,
                    [TrackingKeys.Flow] = TrackingKeys.ShipperNotification,
                    [TrackingKeys.PartyId] = ShipperId,
                };
                if (TransportOrderNumber is not null)
                {
                    keys[TrackingKeys.TransportOrderNumber] = TransportOrderNumber;
                }

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

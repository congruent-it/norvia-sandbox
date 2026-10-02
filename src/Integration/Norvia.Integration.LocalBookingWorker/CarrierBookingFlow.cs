using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.LocalBookingWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum BookingFlowOutcome
{
    /// <summary>The booking was sent and reported sent, or reported not delivered.</summary>
    Done,

    /// <summary>The message isn't a Carrier booking's <c>ShipmentReleased</c>, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The TMS didn't answer the report.</summary>
    TmsUnavailable,

    /// <summary>The TMS refused the report, which sending it again won't change.</summary>
    TmsRefused,
}

/// <summary>
/// The P4 booking Logic App's steps, played locally (ADR 0030): map the release, send it to the Carrier's endpoint under
/// the bounded exponential retry policy, and report the outcome to the TMS under the release's correlation ID.
/// </summary>
internal sealed partial class CarrierBookingFlow(
    CarrierEndpoint carrier,
    TmsReports tms,
    IOptions<BookingWorkerOptions> options,
    TimeProvider time,
    ILogger<CarrierBookingFlow> logger)
{
    public async Task<BookingFlowOutcome> RunAsync(string shipmentReleased, CancellationToken cancellationToken)
    {
        var release = ReleaseOf(shipmentReleased);
        var booking = CarrierBookingMap.ToCarrierBooking(shipmentReleased);
        if (release is null || booking is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "ShipmentReleasedUnreadable"));
            LogUnreadable();
            return BookingFlowOutcome.Unreadable;
        }

        using var trail = logger.BeginScope(release.Keys);
        var answer = await SendAsync(release, booking.ToJsonString(), cancellationToken);

        var status = answer.Succeeded
            ? await tms.ReportSentAsync(release.CarrierBookingNumber, release.CorrelationId, cancellationToken)
            : await tms.ReportNotDeliveredAsync(release.CarrierBookingNumber, release.CorrelationId, answer.StatusCode, answer.Message, cancellationToken);

        using var reported = logger.BeginScope(ReportOutcome(answer, status));
        LogReported(answer.Succeeded ? "sent" : "not delivered", status);
        return status switch
        {
            >= 200 and < 300 => BookingFlowOutcome.Done,
            0 or >= 500 => BookingFlowOutcome.TmsUnavailable,
            _ => BookingFlowOutcome.TmsRefused,
        };
    }

    private async Task<CarrierAnswer> SendAsync(Release release, string booking, CancellationToken cancellationToken)
    {
        if (!options.Value.Carriers.TryGetValue(release.CarrierId, out var endpoint))
        {
            return new(0, $"No booking endpoint is configured for Carrier {release.CarrierId}.");
        }

        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await carrier.SendAsync(endpoint, booking, release.CorrelationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, "CarrierUnavailable"));
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
                : TrackingKeys.OutcomeOf(TrackingKeys.Failed, answer.IsRetryable ? "CarrierUnavailable" : "CarrierRefused");
        }

        return TrackingKeys.OutcomeOf(TrackingKeys.Failed, tmsStatus is 0 or >= 500 ? "TmsUnavailable" : "TmsCallRefused");
    }

    private static Release? ReleaseOf(string shipmentReleased)
    {
        try
        {
            if (JsonNode.Parse(shipmentReleased) is not JsonObject release
                || Text(release, "correlationId") is not { } correlationId
                || Text(release, "carrierBookingNumber") is not { } carrierBookingNumber
                || Text(release, "carrierId") is not { } carrierId)
            {
                return null;
            }

            return new(correlationId, carrierBookingNumber, carrierId, Text(release, "shipmentNumber"), Text(release, "eventId"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't the release of a Carrier booking")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Carrier's endpoint answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported the booking {Report} to the TMS, which answered {StatusCode}")]
    private partial void LogReported(string report, int statusCode);

    /// <summary>What a release names, and the tracking contract's keys it gives the trail.</summary>
    private sealed record Release(string CorrelationId, string CarrierBookingNumber, string CarrierId, string? ShipmentNumber, string? EventId)
    {
        public Dictionary<string, object?> Keys
        {
            get
            {
                var keys = new Dictionary<string, object?>
                {
                    [TrackingKeys.CorrelationId] = CorrelationId,
                    [TrackingKeys.Flow] = TrackingKeys.CarrierBooking,
                    [TrackingKeys.PartyId] = CarrierId,
                    [TrackingKeys.CarrierBookingNumber] = CarrierBookingNumber,
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

using System.Text.Json.Nodes;
using Norvia.Integration.CarrierBooking;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalTrackingWorker;

/// <summary>How the flow's run ended, and so what happens to the queue message: the Logic App's settling steps.</summary>
/// <param name="DeadLetterReason">The dead-letter reason when <see cref="TrackingSettlement.DeadLetter"/>.</param>
/// <param name="DeadLetterDescription">The dead-letter error description when <see cref="TrackingSettlement.DeadLetter"/>.</param>
internal sealed record TrackingRun(TrackingSettlement Settlement, string? DeadLetterReason = null, string? DeadLetterDescription = null)
{
    /// <summary>True when the workflow's run would end Failed: the message wasn't recorded.</summary>
    public bool RunFailed => Settlement != TrackingSettlement.Complete;
}

/// <summary>
/// The Carrier-tracking Logic App's steps, played locally (ADR 0031): map the Carrier's message, record the event with
/// the TMS under the message's correlation ID, and settle the queue message by what the TMS answered.
/// </summary>
internal sealed partial class CarrierTrackingFlow(TmsTrackingEvents tms, ILogger<CarrierTrackingFlow> logger)
{
    /// <summary>The dead-letter reason, and failure code, of a message the map can't read.</summary>
    internal const string UnmappableTrackingMessage = "UnmappableTrackingMessage";

    /// <param name="body">The queue message's body: the Carrier's tracking message as it was sent.</param>
    /// <param name="properties">The message's application properties, which API Management set.</param>
    public async Task<TrackingRun> RunAsync(string body, IReadOnlyDictionary<string, object> properties, CancellationToken cancellationToken, string? messageId = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(properties);
        var correlationId = Property(properties, TrackingKeys.CorrelationId) ?? Guid.NewGuid().ToString();
        var carrierId = Property(properties, TrackingKeys.PartyId);
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = TrackingKeys.CarrierTracking,
        };
        foreach (var name in new[] { TrackingKeys.PartyId, TrackingKeys.SourceMessageId })
        {
            if (Property(properties, name) is { } value)
            {
                keys[name] = value;
            }
        }

        using var trail = logger.BeginScope(keys);
        // The Carrier comes from the message's partyId, which API Management set from the token; without one nothing can be mapped.
        var mapped = carrierId is null ? TrackingMapResult.UnknownStatus(null) : CarrierTrackingMap.Map(body, carrierId);
        if (mapped.TrackingEvent is not { } trackingEvent)
        {
            const string Description = "The Carrier's tracking message could not be mapped: its status code is not in the Carrier tracking code list.";
            using var unmappable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, UnmappableTrackingMessage, messageId));
            LogUnmappable();
            return new(TrackingSettlement.DeadLetter, UnmappableTrackingMessage, Description);
        }

        var (status, answer) = await tms.RecordAsync(trackingEvent.ToJsonString(), correlationId, cancellationToken);
        var outcome = TmsTrackingOutcome.Of(status, answer);

        var recorded = TrackingKeys.OutcomeOf(outcome.Outcome, outcome.FailureCode, messageId);
        if (outcome.ShipmentNumber is not null)
        {
            recorded[TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber;
        }

        if (trackingEvent["carrierBookingNumber"] is JsonValue number && number.TryGetValue<string>(out var bookingNumber))
        {
            recorded[TrackingKeys.CarrierBookingNumber] = bookingNumber;
        }

        using var tmsCall = logger.BeginScope(recorded);
        LogRecorded(status, outcome.Settlement);
        return outcome.Settlement == TrackingSettlement.DeadLetter
            ? new(TrackingSettlement.DeadLetter, outcome.FailureCode, outcome.Description)
            : new(outcome.Settlement);
    }

    private static string? Property(IReadOnlyDictionary<string, object> properties, string name) =>
        properties.TryGetValue(name, out var value) && value?.ToString() is { Length: > 0 } text ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tracking message can't be mapped; it is dead-lettered and the run fails")]
    private partial void LogUnmappable();

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS answered {StatusCode}; the message is settled as {Settlement}")]
    private partial void LogRecorded(int statusCode, TrackingSettlement settlement);
}

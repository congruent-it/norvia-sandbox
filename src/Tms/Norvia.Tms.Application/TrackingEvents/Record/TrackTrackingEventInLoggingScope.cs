using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// Traces a Carrier's Tracking event on its own Message trail (coding guidelines §7): the event's <c>correlationId</c>
/// and <c>partyId</c> from the Carrier that reported it, and for its outcome <c>shipmentNumber</c> and <c>outcome</c>,
/// in the logging scope and on the request's trace. An event the TMS refuses records Refused with its
/// <c>failureCode</c>. A repeat's outcome is traced on the trail of the event it repeats. The API's request scope
/// carries <c>flow</c>.
/// </summary>
public sealed partial class TrackTrackingEventInLoggingScope(ILogger<TrackTrackingEventInLoggingScope> logger)
    : IPipelineBehavior<RecordTrackingCommand, Result<RecordTrackingResult>>
{
    public async ValueTask<Result<RecordTrackingResult>> Handle(
        RecordTrackingCommand message,
        MessageHandlerDelegate<RecordTrackingCommand, Result<RecordTrackingResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        var call = new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = message.CorrelationId };
        if (message.Source.CarrierId is { } carrierId)
        {
            call[TrackingKeys.PartyId] = carrierId.Value;
        }

        using var trail = Track(call);
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(message.CorrelationId, result.Value);
        }
        else
        {
            LogRefusal(message.SourceEventId, result);
        }

        return result;
    }

    private void LogOutcome(string correlationId, RecordTrackingResult outcome)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        };
        if (outcome.CorrelationId != correlationId)
        {
            // A repeat answers on the trail of the event it repeats.
            keys[TrackingKeys.CorrelationId] = outcome.CorrelationId;
        }

        using var scope = Track(keys);
        if (outcome.Repeat)
        {
            LogRepeat(logger, outcome.EventId, outcome.ShipmentNumber);
        }
        else
        {
            LogRecorded(logger, outcome.EventId, outcome.ShipmentNumber, outcome.ShipmentStatus, outcome.MovedShipment);
        }
    }

    private void LogRefusal(string sourceEventId, Result<RecordTrackingResult> result)
    {
        var refusal = result.ValidationErrors.First();
        using var refused = Track(new()
        {
            [TrackingKeys.Outcome] = TrackingKeys.Refused,
            [TrackingKeys.FailureCode] = refusal.ErrorCode,
        });
        LogRefused(logger, sourceEventId, refusal.ErrorMessage);
    }

    private IDisposable? Track(Dictionary<string, object?> keys)
    {
        foreach (var (key, value) in keys)
        {
            Activity.Current?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking event {SourceEventId} recorded for Shipment {ShipmentNumber}, now {ShipmentStatus} (moved it: {MovedShipment})")]
    private static partial void LogRecorded(ILogger logger, string sourceEventId, string shipmentNumber, ShipmentStatus shipmentStatus, bool movedShipment);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking event {SourceEventId} for Shipment {ShipmentNumber} recorded again as a repeat")]
    private static partial void LogRepeat(ILogger logger, string sourceEventId, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking event {SourceEventId} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string sourceEventId, string reason);
}

using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

/// <summary>
/// Traces a Carrier's or a Driver's Proof of delivery on its own Message trail (coding guidelines §7): the call's <c>correlationId</c>,
/// <c>partyId</c> from the Carrier that sent it and <c>sourceMessageId</c>, the sender's own document ID, and for its
/// outcome <c>shipmentNumber</c> and <c>outcome</c>, in the logging scope and on the request's trace. A Proof of delivery
/// the TMS refuses records Refused with its <c>failureCode</c>. The API's request scope carries <c>flow</c>, which a Driver's
/// Proof of delivery (P12) changes to <c>trip-execution</c> with its <c>tripNumber</c>.
/// </summary>
public sealed partial class TrackProofOfDeliveryInLoggingScope(ILogger<TrackProofOfDeliveryInLoggingScope> logger)
    : IPipelineBehavior<RecordProofOfDeliveryCommand, Result<RecordProofOfDeliveryResult>>
{
    public async ValueTask<Result<RecordProofOfDeliveryResult>> Handle(
        RecordProofOfDeliveryCommand message,
        MessageHandlerDelegate<RecordProofOfDeliveryCommand, Result<RecordProofOfDeliveryResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        var call = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.SourceMessageId] = message.Report.DocumentId,
        };
        if (message.Report.Source.CarrierId is { } carrierId)
        {
            call[TrackingKeys.PartyId] = carrierId.Value;
        }

        if (message.Report.OwnFleet is { } own)
        {
            call[TrackingKeys.Flow] = Flows.TripExecution;
            call[TrackingKeys.TripNumber] = own.TripNumber.Value;
            call[TrackingKeys.ShipmentNumber] = own.ShipmentNumber.Value;
        }

        using var trail = logger.Track(call);
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(result.Value);
        }
        else
        {
            LogRefusal(message.Report.DocumentId, result);
        }

        return result;
    }

    private void LogOutcome(RecordProofOfDeliveryResult outcome)
    {
        using var scope = logger.Track(new()
        {
            [TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        });
        if (outcome.IsRepeat)
        {
            LogRepeated(logger, outcome.DocumentId, outcome.ShipmentNumber);
            return;
        }

        LogRecorded(logger, outcome.DocumentId, outcome.ShipmentNumber, outcome.ShipmentStatus, outcome.NumberOfFiles);
    }

    private void LogRefusal(string documentId, Result<RecordProofOfDeliveryResult> result)
    {
        var refusal = result.ValidationErrors.First();
        using var refused = logger.Track(new()
        {
            [TrackingKeys.Outcome] = TrackingKeys.Refused,
            [TrackingKeys.FailureCode] = refusal.ErrorCode,
        });
        LogRefused(logger, documentId, refusal.ErrorMessage);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Proof of delivery {DocumentId} recorded for Shipment {ShipmentNumber}, which stays {ShipmentStatus} ({NumberOfFiles} files)")]
    private static partial void LogRecorded(ILogger logger, string documentId, string shipmentNumber, ShipmentStatus shipmentStatus, int numberOfFiles);

    [LoggerMessage(Level = LogLevel.Information, Message = "Proof of delivery {DocumentId} for Shipment {ShipmentNumber} was recorded before: nothing published")]
    private static partial void LogRepeated(ILogger logger, string documentId, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Proof of delivery {DocumentId} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string documentId, string reason);
}

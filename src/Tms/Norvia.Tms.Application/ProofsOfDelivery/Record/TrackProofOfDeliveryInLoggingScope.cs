using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

/// <summary>
/// Traces a Carrier's Proof of delivery on its own Message trail (coding guidelines §7): the call's <c>correlationId</c>,
/// <c>partyId</c> from the Carrier that sent it and <c>sourceMessageId</c>, the Carrier's own document ID, and for its
/// outcome <c>shipmentNumber</c> and <c>outcome</c>, in the logging scope and on the request's trace. A Proof of delivery
/// the TMS refuses records Refused with its <c>failureCode</c>. The API's request scope carries <c>flow</c>.
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

        using var trail = Track(call);
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
        using var scope = Track(new()
        {
            [TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        });
        LogRecorded(logger, outcome.DocumentId, outcome.ShipmentNumber, outcome.ShipmentStatus, outcome.NumberOfFiles);
    }

    private void LogRefusal(string documentId, Result<RecordProofOfDeliveryResult> result)
    {
        var refusal = result.ValidationErrors.First();
        using var refused = Track(new()
        {
            [TrackingKeys.Outcome] = TrackingKeys.Refused,
            [TrackingKeys.FailureCode] = refusal.ErrorCode,
        });
        LogRefused(logger, documentId, refusal.ErrorMessage);
    }

    private IDisposable? Track(Dictionary<string, object?> keys)
    {
        foreach (var (key, value) in keys)
        {
            Activity.Current?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Proof of delivery {DocumentId} recorded for Shipment {ShipmentNumber}, which stays {ShipmentStatus} ({NumberOfFiles} files)")]
    private static partial void LogRecorded(ILogger logger, string documentId, string shipmentNumber, ShipmentStatus shipmentStatus, int numberOfFiles);

    [LoggerMessage(Level = LogLevel.Information, Message = "Proof of delivery {DocumentId} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string documentId, string reason);
}

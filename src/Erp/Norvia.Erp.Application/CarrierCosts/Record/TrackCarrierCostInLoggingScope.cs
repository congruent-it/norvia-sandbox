using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.CarrierCosts.Record;

/// <summary>
/// Traces the recording of a Carrier cost on the trail it continues, that of the Delivered Tracking event (coding guidelines §7):
/// <c>correlationId</c>, <c>carrierBookingNumber</c>, <c>shipmentNumber</c>, <c>transportOrderNumber</c> and <c>partyId</c> (the Carrier)
/// for the whole call, and <c>outcome</c> for its outcome. A repeat records Succeeded, as it is the original Carrier cost again; a
/// refusal records Refused with its <c>failureCode</c>. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackCarrierCostInLoggingScope(ILogger<TrackCarrierCostInLoggingScope> logger)
    : IPipelineBehavior<RecordCarrierCostCommand, Result<RecordCarrierCostResult>>
{
    public async ValueTask<Result<RecordCarrierCostResult>> Handle(
        RecordCarrierCostCommand message,
        MessageHandlerDelegate<RecordCarrierCostCommand, Result<RecordCarrierCostResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = message.Cost.CorrelationId,
            [TrackingKeys.CarrierBookingNumber] = message.Cost.CarrierBookingNumber,
            [TrackingKeys.ShipmentNumber] = message.Cost.ShipmentNumber,
            [TrackingKeys.TransportOrderNumber] = message.Cost.TransportOrderNumber,
            [TrackingKeys.PartyId] = message.Cost.CarrierId.Value,
        });

        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            if (result.Value.Repeat)
            {
                LogRepeat(logger, message.Cost.CarrierBookingNumber);
            }
            else
            {
                LogRecorded(logger, message.Cost.CarrierBookingNumber);
            }
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, message.Cost.CarrierBookingNumber, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier cost for Carrier booking {CarrierBookingNumber} recorded as Open")]
    private static partial void LogRecorded(ILogger logger, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier cost for Carrier booking {CarrierBookingNumber} was already recorded; answering with the original")]
    private static partial void LogRepeat(ILogger logger, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier cost for Carrier booking {CarrierBookingNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string carrierBookingNumber, string reason);
}

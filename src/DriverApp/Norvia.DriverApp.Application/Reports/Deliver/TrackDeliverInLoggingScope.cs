using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Faults;

namespace Norvia.DriverApp.Application.Reports.Deliver;

/// <summary>
/// Traces a Driver's Deliver on the trail of the Driver's action (coding guidelines §7), as <see cref="ReportStop.TrackReportStopInLoggingScope"/> does for
/// the other Stop reports: the <c>flow</c>, the <c>tripNumber</c>, the Shipment, the Driver as <c>partyId</c> and, once the Deliver is recorded, its
/// <c>sourceMessageId</c> (the Driver app event ID of the Delivered), then the <c>outcome</c>. A Deliver recorded while the HEIC fault is on carries
/// the fault's name as its <c>fault</c>: the hop succeeded, and the TMS refuses what it stored.
/// </summary>
public sealed partial class TrackDeliverInLoggingScope(ILogger<TrackDeliverInLoggingScope> logger, IDriverAppFaults faults) : IPipelineBehavior<DeliverCommand, Result<DeliverResult>>
{
    public async ValueTask<Result<DeliverResult>> Handle(
        DeliverCommand message, MessageHandlerDelegate<DeliverCommand, Result<DeliverResult>> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.Flow] = Flows.TripExecution,
            [TrackingKeys.TripNumber] = message.TripNumber,
            [TrackingKeys.ShipmentNumber] = message.ShipmentNumber,
            [TrackingKeys.PartyId] = message.DriverId,
        });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            var keys = new Dictionary<string, object?>
            {
                [TrackingKeys.SourceMessageId] = result.Value.ReportId.ToString(),
                [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
            };
            if (faults.HeicPhoto && !result.Value.AlreadyReported)
            {
                keys[TrackingKeys.Fault] = DriverAppFaultNames.HeicPhoto;
            }

            using var recorded = logger.Track(keys);
            LogRecorded(logger, message.ShipmentNumber, message.TripNumber, result.Value.AlreadyReported);
        }
        else
        {
            var code = result.Status switch
            {
                ResultStatus.NotFound => TrackingFailureCodes.StopNotFound,
                ResultStatus.Invalid => TrackingFailureCodes.ProofOfDeliveryInvalid,
                _ => TrackingFailureCodes.StopNotAvailable,
            };
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = code });
            LogRefused(logger, message.ShipmentNumber, message.TripNumber, code);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver delivered {ShipmentNumber} on Trip {TripNumber} with a Proof of delivery (already reported: {AlreadyReported})")]
    private static partial void LogRecorded(ILogger logger, string shipmentNumber, string tripNumber, bool alreadyReported);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver's delivery of {ShipmentNumber} on Trip {TripNumber} was not taken: {FailureCode}")]
    private static partial void LogRefused(ILogger logger, string shipmentNumber, string tripNumber, string failureCode);
}

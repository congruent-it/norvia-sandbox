using Microsoft.Extensions.Logging;

namespace Norvia.Tms.Application.Exceptions.CancelCarrierBooking;

/// <summary>Traces the Planner's cancellation of a Carrier booking on its own Message trail (<see cref="TrackExceptionActionInLoggingScope{TCommand, TResult}"/>).</summary>
public sealed partial class TrackCarrierBookingCancellationInLoggingScope(ILogger<TrackCarrierBookingCancellationInLoggingScope> logger)
    : TrackExceptionActionInLoggingScope<CancelCarrierBookingCommand, CancelCarrierBookingResult>
{
    protected override ILogger Logger => logger;

    protected override string CorrelationIdOf(CancelCarrierBookingCommand command) => command.CorrelationId;

    protected override string ShipmentNumberOf(CancelCarrierBookingResult result) => result.ShipmentNumber;

    protected override void LogSucceeded(CancelCarrierBookingResult result) =>
        LogCancelled(logger, result.CarrierBookingNumber, result.ExceptionNumber, result.ShipmentNumber);

    protected override void LogRefused(CancelCarrierBookingCommand command, string reason) => LogRefused(logger, command.ExceptionNumber, reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} cancelled from Exception {ExceptionNumber} of Shipment {ShipmentNumber}")]
    private static partial void LogCancelled(ILogger logger, string carrierBookingNumber, string exceptionNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelling a Carrier booking from Exception {ExceptionNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string exceptionNumber, string reason);
}

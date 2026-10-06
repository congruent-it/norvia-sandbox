using Ardalis.Result;
using Microsoft.Extensions.Logging;

namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

/// <summary>Traces the Planner's agreed delivery window on its own Message trail (<see cref="TrackExceptionActionInLoggingScope{TCommand, TResult}"/>).</summary>
public sealed partial class TrackDeliveryWindowAgreementInLoggingScope(ILogger<TrackDeliveryWindowAgreementInLoggingScope> logger)
    : TrackExceptionActionInLoggingScope<AgreeDeliveryWindowCommand, AgreeDeliveryWindowResult>
{
    protected override ILogger Logger => logger;

    protected override string CorrelationIdOf(AgreeDeliveryWindowCommand command) => command.CorrelationId;

    protected override string ShipmentNumberOf(AgreeDeliveryWindowResult result) => result.ShipmentNumber;

    protected override void LogSucceeded(AgreeDeliveryWindowResult result) => LogAgreed(logger, result.ExceptionNumber, result.ShipmentNumber);

    protected override void LogRefused(AgreeDeliveryWindowCommand command, string reason) => LogRefused(logger, command.ExceptionNumber, reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivery window agreed on Exception {ExceptionNumber} of Shipment {ShipmentNumber}")]
    private static partial void LogAgreed(ILogger logger, string exceptionNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivery window on Exception {ExceptionNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string exceptionNumber, string reason);
}

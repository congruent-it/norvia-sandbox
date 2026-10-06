using Microsoft.Extensions.Logging;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Resolve;

/// <summary>Traces a Planner's resolution of an Exception on its own Message trail (<see cref="TrackExceptionActionInLoggingScope{TCommand, TResult}"/>).</summary>
public sealed partial class TrackExceptionResolutionInLoggingScope(ILogger<TrackExceptionResolutionInLoggingScope> logger)
    : TrackExceptionActionInLoggingScope<ResolveExceptionCommand, ResolveExceptionResult>
{
    protected override ILogger Logger => logger;

    protected override string CorrelationIdOf(ResolveExceptionCommand command) => command.CorrelationId;

    protected override string ShipmentNumberOf(ResolveExceptionResult result) => result.ShipmentNumber;

    protected override void LogSucceeded(ResolveExceptionResult result) => LogResolved(logger, result.ExceptionNumber, result.ShipmentNumber, result.Outcome);

    protected override void LogRefused(ResolveExceptionCommand command, string reason) => LogRefused(logger, command.ExceptionNumber, reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exception {ExceptionNumber} of Shipment {ShipmentNumber} resolved as {Outcome}")]
    private static partial void LogResolved(ILogger logger, string exceptionNumber, string shipmentNumber, ExceptionOutcome outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolution of Exception {ExceptionNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string exceptionNumber, string reason);
}

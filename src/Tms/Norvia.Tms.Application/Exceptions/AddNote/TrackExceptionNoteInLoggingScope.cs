using Microsoft.Extensions.Logging;

namespace Norvia.Tms.Application.Exceptions.AddNote;

/// <summary>Traces a Planner's note on an Exception on its own Message trail (<see cref="TrackExceptionActionInLoggingScope{TCommand, TResult}"/>).</summary>
public sealed partial class TrackExceptionNoteInLoggingScope(ILogger<TrackExceptionNoteInLoggingScope> logger)
    : TrackExceptionActionInLoggingScope<AddExceptionNoteCommand, AddExceptionNoteResult>
{
    protected override ILogger Logger => logger;

    protected override string CorrelationIdOf(AddExceptionNoteCommand command) => command.CorrelationId;

    protected override string ShipmentNumberOf(AddExceptionNoteResult result) => result.ShipmentNumber;

    protected override void LogSucceeded(AddExceptionNoteResult result) => LogAdded(logger, result.ExceptionNumber, result.ShipmentNumber);

    protected override void LogRefused(AddExceptionNoteCommand command, string reason) => LogRefused(logger, command.ExceptionNumber, reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Note added to Exception {ExceptionNumber} of Shipment {ShipmentNumber}")]
    private static partial void LogAdded(ILogger logger, string exceptionNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Note on Exception {ExceptionNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string exceptionNumber, string reason);
}

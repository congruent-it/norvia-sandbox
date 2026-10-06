using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Exceptions.AddNote;

/// <summary>
/// Traces a Planner's note on an Exception on its own Message trail (coding guidelines §7): the <c>correlationId</c> the TMS
/// created for it for the whole call, and for its outcome <c>shipmentNumber</c>, in the logging scope and on the request's
/// trace. A note the TMS refuses records Refused with its <c>failureCode</c>. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackExceptionNoteInLoggingScope(ILogger<TrackExceptionNoteInLoggingScope> logger)
    : IPipelineBehavior<AddExceptionNoteCommand, Result<AddExceptionNoteResult>>
{
    public async ValueTask<Result<AddExceptionNoteResult>> Handle(
        AddExceptionNoteCommand message,
        MessageHandlerDelegate<AddExceptionNoteCommand, Result<AddExceptionNoteResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new() { [TrackingKeys.CorrelationId] = message.CorrelationId });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = logger.Track(new()
            {
                [TrackingKeys.ShipmentNumber] = result.Value.ShipmentNumber,
                [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
            });
            LogAdded(logger, result.Value.ExceptionNumber, result.Value.ShipmentNumber);
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new()
            {
                [TrackingKeys.Outcome] = TrackingKeys.Refused,
                [TrackingKeys.FailureCode] = refusal.ErrorCode,
            });
            LogRefused(logger, message.ExceptionNumber, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Note added to Exception {ExceptionNumber} of Shipment {ShipmentNumber}")]
    private static partial void LogAdded(ILogger logger, string exceptionNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Note on Exception {ExceptionNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string exceptionNumber, string reason);
}

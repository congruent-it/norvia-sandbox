namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>
/// The dead-letter pages' logs. Here rather than in the page: the logging source generator can't see Razor's generated
/// code. They name the queue and the sequence number only, never a body or a property (docs/operations/integration-console.md).
/// </summary>
internal static partial class DeadLetterPageLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Listing the dead-letter queues failed")]
    public static partial void ListFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Listing the dead-lettered messages of {Entity} failed")]
    public static partial void MessagesFailed(ILogger logger, Exception exception, string entity);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the dead-lettered message {SequenceNumber} of {Entity} failed")]
    public static partial void OpenFailed(ILogger logger, Exception exception, long sequenceNumber, string entity);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resubmitting the dead-lettered message {SequenceNumber} of {Entity} failed")]
    public static partial void ResubmitFailed(ILogger logger, Exception exception, long sequenceNumber, string entity);
}

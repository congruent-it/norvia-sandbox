namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>The dead-letter pages' logs. Here rather than in the page: the logging source generator can't see Razor's generated code.</summary>
internal static partial class DeadLetterPageLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Listing the dead-letter queues failed")]
    public static partial void ListFailed(ILogger logger, Exception exception);
}

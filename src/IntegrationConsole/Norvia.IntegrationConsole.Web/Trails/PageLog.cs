namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>
/// The pages' logs. Here rather than in the pages: the logging source generator can't see Razor's generated code. They
/// log keys only, never a message body (docs/operations/integration-console.md).
/// </summary>
internal static partial class PageLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Searching Message trails for {Key} failed")]
    public static partial void SearchFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the Message trail {CorrelationId} failed")]
    public static partial void OpenFailed(ILogger logger, Exception exception, string correlationId);
}

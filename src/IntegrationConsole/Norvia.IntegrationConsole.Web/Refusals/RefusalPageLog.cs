namespace Norvia.IntegrationConsole.Web.Refusals;

/// <summary>The refused-at-the-edge page's logs. Here rather than in the page: the logging source generator can't see Razor's generated code.</summary>
internal static partial class RefusalPageLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Listing the calls refused at the edge failed")]
    public static partial void ListFailed(ILogger logger, Exception exception);
}

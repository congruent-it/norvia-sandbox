namespace Norvia.IntegrationConsole.Web.Runs;

/// <summary>
/// The run detail page's logs. Here rather than in the page: the logging source generator can't see Razor's generated
/// code. They name the run and the step only, never a body (docs/operations/integration-console.md).
/// </summary>
internal static partial class RunPageLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the run {RunId} of {Workflow} on {LogicApp} failed")]
    public static partial void OpenFailed(ILogger logger, Exception exception, string runId, string workflow, string logicApp);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fetching the bodies of step {Step} of the run {RunId} failed")]
    public static partial void StepFailed(ILogger logger, Exception exception, string step, string runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Listing the failed runs failed")]
    public static partial void ListFailed(ILogger logger, Exception exception);
}

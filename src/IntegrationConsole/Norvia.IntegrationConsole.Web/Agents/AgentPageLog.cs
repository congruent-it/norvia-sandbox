namespace Norvia.IntegrationConsole.Web.Agents;

/// <summary>The Explain button's logs. Here rather than in the component: the logging source generator can't see Razor's generated code.</summary>
internal static partial class AgentPageLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining the Message trail {CorrelationId} failed with {FailureCode}")]
    public static partial void ExplainFailed(ILogger logger, string correlationId, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining the Message trail {CorrelationId} failed")]
    public static partial void ExplainCrashed(ILogger logger, Exception exception, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining {Subject} failed with {FailureCode}")]
    public static partial void ExplainItemFailed(ILogger logger, string subject, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining {Subject} failed")]
    public static partial void ExplainItemCrashed(ILogger logger, Exception exception, string subject);
}

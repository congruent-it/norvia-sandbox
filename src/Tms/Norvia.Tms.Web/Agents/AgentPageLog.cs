namespace Norvia.Tms.Web.Agents;

/// <summary>The Explain button's logs. Here rather than in the component: the logging source generator can't see Razor's generated code.</summary>
internal static partial class AgentPageLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining Rejected booking {TransportOrderId} failed with {FailureCode}")]
    public static partial void ExplainFailed(ILogger logger, Guid transportOrderId, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining Rejected booking {TransportOrderId} failed")]
    public static partial void ExplainCrashed(ILogger logger, Exception exception, Guid transportOrderId);
}

namespace Norvia.Tms.Web.Agents;

/// <summary>The Explain button's logs. Here rather than in the component: the logging source generator can't see Razor's generated code.</summary>
internal static partial class AgentPageLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining Rejected booking {TransportOrderId} failed with {FailureCode}")]
    public static partial void ExplainFailed(ILogger logger, Guid transportOrderId, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining Rejected booking {TransportOrderId} failed")]
    public static partial void ExplainCrashed(ILogger logger, Exception exception, Guid transportOrderId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining Transport Order {TransportOrderNumber} failed with {FailureCode}")]
    public static partial void ExplainOrderFailed(ILogger logger, string transportOrderNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recommending a release of Transport Order {TransportOrderNumber} failed with {FailureCode}")]
    public static partial void RecommendFailed(ILogger logger, string transportOrderNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recommending a release of Transport Order {TransportOrderNumber} failed")]
    public static partial void RecommendCrashed(ILogger logger, Exception exception, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining Transport Order {TransportOrderNumber} failed")]
    public static partial void ExplainOrderCrashed(ILogger logger, Exception exception, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drafting a reply about Transport Order {TransportOrderNumber} failed with {FailureCode}")]
    public static partial void DraftFailed(ILogger logger, string transportOrderNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drafting a reply about Transport Order {TransportOrderNumber} failed")]
    public static partial void DraftCrashed(ILogger logger, Exception exception, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suggesting an assignment for Transport Order {TransportOrderNumber} failed with {FailureCode}")]
    public static partial void SuggestFailed(ILogger logger, string transportOrderNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Suggesting an assignment for Transport Order {TransportOrderNumber} failed")]
    public static partial void SuggestCrashed(ILogger logger, Exception exception, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drafting a Shipper notification for Exception {ExceptionNumber} failed with {FailureCode}")]
    public static partial void NotificationDraftFailed(ILogger logger, string exceptionNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drafting a Shipper notification for Exception {ExceptionNumber} failed")]
    public static partial void NotificationDraftCrashed(ILogger logger, Exception exception, string exceptionNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drafting a note on Exception {ExceptionNumber} failed with {FailureCode}")]
    public static partial void NoteDraftFailed(ILogger logger, string exceptionNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drafting a note on Exception {ExceptionNumber} failed")]
    public static partial void NoteDraftCrashed(ILogger logger, Exception exception, string exceptionNumber);
}

namespace Norvia.HealthCheck.Web.Checks;

/// <summary>The page's logs. Here rather than in the component: the logging source generator can't see Razor's generated code.</summary>
internal static partial class PageLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Narrating the health check {CorrelationId} failed with {FailureCode}")]
    public static partial void NarrationFailed(ILogger logger, string correlationId, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Narrating the health check {CorrelationId} failed")]
    public static partial void NarrationCrashed(ILogger logger, Exception exception, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining the spend {CorrelationId} failed with {FailureCode}")]
    public static partial void SpendNarrationFailed(ILogger logger, string correlationId, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining the spend {CorrelationId} failed")]
    public static partial void SpendNarrationCrashed(ILogger logger, Exception exception, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the estate's spend failed")]
    public static partial void CostCrashed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Running the health check {CorrelationId} failed")]
    public static partial void RunCrashed(ILogger logger, Exception exception, string correlationId);
}

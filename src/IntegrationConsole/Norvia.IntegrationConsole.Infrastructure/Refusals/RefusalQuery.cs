using Norvia.IntegrationConsole.Application.Refusals;

namespace Norvia.IntegrationConsole.Infrastructure.Refusals;

/// <summary>
/// The KQL that finds the calls API Management refused at the edge: its request telemetry with a result code of 401, 403 or
/// 429, which holds every refusal, the gateway's own 401 for a subscription key no policy sees included. The policies' traces
/// with outcome <c>Refused</c> add the failure code where a policy ran; a 401 with no such trace is the
/// gateway's, with <see cref="SubscriptionKeyRejected"/> as its failure code. Only keys are read, never a body, a token or a key.
/// The time range is the query's own, set on the request.
/// </summary>
internal static class RefusalQuery
{
    /// <summary>The failure code of a call the gateway refused before any policy ran, a missing or wrong subscription key.</summary>
    public const string SubscriptionKeyRejected = "SubscriptionKeyRejected";

    public const int Maximum = 1000;

    public static readonly string Text = $$"""
        let policy = AppTraces
            | where tostring(Properties.outcome) =~ "Refused"
            | project OperationId, failureCode = tostring(Properties.failureCode);
        AppRequests
        | where AppRoleName startswith "apim" and ResultCode in ({{string.Join(", ", RefusalStatus.All.Select(status => $"\"{status}\""))}})
        | join kind=leftouter (policy | summarize failureCode = take_any(failureCode) by OperationId) on OperationId
        | project time = TimeGenerated, operation = Name, statusCode = toint(ResultCode),
            failureCode = iff(isnotempty(failureCode), failureCode, case(ResultCode == "401", "{{SubscriptionKeyRejected}}", ResultCode == "429", "RateLimitExceeded", "Forbidden")),
            caller = coalesce(tostring(Properties.azp), tostring(Properties["Subscription Name"])),
            correlationId = coalesce(tostring(Properties["Response-X-Correlation-ID"]), tostring(Properties["Request-X-Correlation-ID"]), tostring(Properties.correlationId))
        | top {{Maximum}} by time desc
        """;
}

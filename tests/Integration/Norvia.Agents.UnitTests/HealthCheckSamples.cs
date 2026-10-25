namespace Norvia.Agents.UnitTests;

/// <summary>What the health-check API's <c>run_checks</c> returns, as the Estate health-check agent reads it.</summary>
internal static class HealthCheckSamples
{
    public const string OneFinding = """
        {"estate":"as-found","checksRun":1,"findings":[{"id":"F-DLQ-ALERT","checkId":"DLQ-ALERT","title":"A dead-letter queue has no alert","rule":"Every queue has an alert.","severity":"High","evidence":[{"subject":"billing","detail":"No alert."},{"subject":"carrier-tracking","detail":"No alert."}],"asFoundToggle":"entitiesWithoutDeadLetterAlert","fix":"Remove it."}]}
        """;

    public const string NoCarrierTracking = """
        {"estate":"as-found","checksRun":1,"findings":[{"id":"F-DLQ-ALERT","checkId":"DLQ-ALERT","title":"A dead-letter queue has no alert","rule":"Every queue has an alert.","severity":"High","evidence":[{"subject":"billing","detail":"No alert."}],"asFoundToggle":"entitiesWithoutDeadLetterAlert","fix":"Remove it."}]}
        """;

    public const string TwoChecks = """
        {"estate":"as-found","checksRun":2,"findings":[
          {"id":"F-LOW-ONE","checkId":"LOW-ONE","title":"Something small","rule":"A small rule.","severity":"Low","evidence":[],"asFoundToggle":"t","fix":"f"},
          {"id":"F-CRIT-ONE","checkId":"CRIT-ONE","title":"Something big","rule":"A big rule.","severity":"Critical","evidence":[{"subject":"x","detail":"y"}],"asFoundToggle":"t","fix":"f"}]}
        """;

    public const string NoFinding = """{"estate":"fixed","checksRun":1,"findings":[]}""";
}

namespace Norvia.Agents.UnitTests;

internal static class TrailSamples
{
    /// <summary>P5: a Carrier's Delivered event with DLV instead of POD, dead-lettered by the Carrier-tracking flow.</summary>
    public const string UnmappableCode = """
        {"correlationId":"c-1","hops":[
          {"name":"partner-api: POST /v1/tracking","flow":"carrier-tracking","outcome":"Succeeded"},
          {"name":"tracking-worker: carrier-tracking","flow":"carrier-tracking","outcome":"DeadLettered","failureCode":"UnmappableTrackingMessage"}],
         "businessKeys":[]}
        """;

    public const string FailedRun = """
        {"correlationId":"c-1","hops":[
          {"name":"logic-booking: carrier-booking","flow":"carrier-booking","outcome":"Failed","failureCode":"CarrierRefused",
           "run":{"logicApp":"logic-booking","workflow":"carrier-booking","runId":"08584"}}],
         "businessKeys":[]}
        """;

    public const string AllWell = """
        {"correlationId":"c-1","hops":[{"name":"tms-api: POST /v1/orders","flow":"transport-order-intake","outcome":"Succeeded"}],"businessKeys":[]}
        """;

    public const string Run = """
        {"status":"Failed","steps":[{"name":"Call_Carrier","status":"Failed","error":{"code":"BadRequest","message":"refused"}}]}
        """;
}

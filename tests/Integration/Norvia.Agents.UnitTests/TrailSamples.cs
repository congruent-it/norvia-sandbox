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

    public const string FailedRuns = """
        {"from":"2026-10-23T10:00:00+00:00","to":"2026-10-24T10:00:00+00:00","runs":[
          {"flow":"carrier-booking","run":{"logicApp":"logic-booking","workflow":"carrier-booking","runId":"08584"},"start":"2026-10-24T09:30:00+00:00",
           "correlationId":"c-9","failedStep":"Call_Carrier","error":{"code":"BadRequest","message":"refused"}},
          {"flow":"billing","run":null,"start":"2026-10-24T08:00:00+00:00","correlationId":null,"failedStep":null,"error":null}]}
        """;

    public const string DeadLetterQueues = """
        {"at":"2026-10-24T10:00:00+00:00","queues":[
          {"name":"carrier-tracking","count":3,"oldestAgeSeconds":7200},
          {"name":"shipment-events/billing","count":1,"oldestAgeSeconds":60},
          {"name":"transport-orders","count":0,"oldestAgeSeconds":null}]}
        """;

    public const string PeekedTracking = """
        {"entity":"carrier-tracking","hasMore":false,"messages":[
          {"sequenceNumber":4,"enqueuedAt":"2026-10-24T08:00:00+00:00","reason":"UnmappableTrackingMessage","description":"Unknown status code DLV","deliveryCount":1,
           "correlationId":"c-1","businessKeys":[],"applicationProperties":{"correlationId":"c-1"}},
          {"sequenceNumber":5,"enqueuedAt":"2026-10-24T08:05:00+00:00","reason":"UnmappableTrackingMessage","description":"Unknown status code XYZ","deliveryCount":1,
           "correlationId":"c-2","businessKeys":[],"applicationProperties":{}},
          {"sequenceNumber":6,"enqueuedAt":"2026-10-24T08:10:00+00:00","reason":"MaxDeliveryCountExceeded","description":"Failed 10 times","deliveryCount":10,
           "correlationId":null,"businessKeys":[],"applicationProperties":{}}]}
        """;

    public const string PeekedBilling = """
        {"entity":"shipment-events/billing","hasMore":false,"messages":[
          {"sequenceNumber":1,"enqueuedAt":"2026-10-24T09:59:00+00:00","reason":"MaxDeliveryCountExceeded","description":"Failed 10 times","deliveryCount":10,
           "correlationId":null,"businessKeys":[],"applicationProperties":{}}]}
        """;

    public const string NoDeadLetters = """
        {"at":"2026-10-24T10:00:00+00:00","queues":[{"name":"carrier-tracking","count":0,"oldestAgeSeconds":null}]}
        """;
}

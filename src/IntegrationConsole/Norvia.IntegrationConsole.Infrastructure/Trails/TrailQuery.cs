using Azure.Monitor.Query.Logs;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// The KQL that finds Message trails in the workspace, by the tracking contract's keys (coding guidelines §7). Each
/// source records them in its own place:
/// <list type="bullet">
/// <item>applications, in their logging scope and on their request's trace: straight in <c>Properties</c>;</item>
/// <item>API Management, as the metadata of its <c>trace</c> policies, also in <c>Properties</c>, and the correlation
/// ID as the logged <c>X-Correlation-ID</c> header of its requests;</item>
/// <item>Logic Apps Standard, as tracked properties: a JSON string in <c>Properties.trackedProperties</c>.</item>
/// </list>
/// The time range is the query's own, set on the request. Only keys are read, never a column that could hold a body.
/// </summary>
internal static class TrailQuery
{
    public static string For(string key, int maximumTrails) =>
        LogsQueryClient.CreateQuery($"let searched = {key};") + $"\nlet maximumTrails = {maximumTrails};\n" + Body;

    private const string Body = """
        let keysOf = (rows:(TimeGenerated:datetime, Properties:dynamic)) {
            rows
            | extend tracked = parse_json(tostring(Properties.trackedProperties))
            | project TimeGenerated,
                correlationId = coalesce(tostring(Properties.correlationId), tostring(tracked.correlationId),
                    tostring(Properties["Response-X-Correlation-ID"]), tostring(Properties["Request-X-Correlation-ID"])),
                flow = coalesce(tostring(Properties.flow), tostring(tracked.flow)),
                outcome = coalesce(tostring(Properties.outcome), tostring(tracked.outcome)),
                transportOrderNumber = coalesce(tostring(Properties.transportOrderNumber), tostring(tracked.transportOrderNumber)),
                shipmentNumber = coalesce(tostring(Properties.shipmentNumber), tostring(tracked.shipmentNumber)),
                carrierBookingNumber = coalesce(tostring(Properties.carrierBookingNumber), tostring(tracked.carrierBookingNumber)),
                tripNumber = coalesce(tostring(Properties.tripNumber), tostring(tracked.tripNumber)),
                sourceMessageId = coalesce(tostring(Properties.sourceMessageId), tostring(tracked.sourceMessageId))
            | where isnotempty(correlationId)
        };
        let telemetry = union isfuzzy=true AppRequests, AppDependencies, AppTraces, AppExceptions
            | project TimeGenerated, Properties;
        // The term index narrows each pass before any JSON is parsed: first to the rows mentioning the key, then to
        // the rows of the trails that carried it.
        let found = toscalar(keysOf(telemetry | where Properties has searched)
            | where correlationId =~ searched or transportOrderNumber =~ searched or shipmentNumber =~ searched
                or carrierBookingNumber =~ searched or tripNumber =~ searched or sourceMessageId =~ searched
            | summarize make_set(correlationId));
        keysOf(telemetry | where Properties has_any (found))
        | where correlationId in (found)
        | summarize firstSeen = min(TimeGenerated), lastSeen = max(TimeGenerated),
            flows = tostring(make_set_if(flow, isnotempty(flow))),
            outcomes = tostring(make_set_if(outcome, isnotempty(outcome))),
            transportOrderNumbers = tostring(make_set_if(transportOrderNumber, isnotempty(transportOrderNumber))),
            shipmentNumbers = tostring(make_set_if(shipmentNumber, isnotempty(shipmentNumber))),
            carrierBookingNumbers = tostring(make_set_if(carrierBookingNumber, isnotempty(carrierBookingNumber))),
            tripNumbers = tostring(make_set_if(tripNumber, isnotempty(tripNumber))),
            sourceMessageIds = tostring(make_set_if(sourceMessageId, isnotempty(sourceMessageId)))
            by correlationId
        | top maximumTrails by lastSeen desc
        """;
}

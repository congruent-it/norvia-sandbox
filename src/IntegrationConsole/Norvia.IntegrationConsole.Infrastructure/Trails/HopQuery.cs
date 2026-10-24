using Azure.Monitor.Query.Logs;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// The KQL that finds a Message trail's hops in the workspace, by its correlation ID. It returns rows of four kinds,
/// which <see cref="LogAnalyticsTrailSource"/> puts together into hops:
/// <list type="bullet">
/// <item><c>request</c>: a call to API Management or an application that recorded the correlation ID, in its own
/// properties or, for API Management, as its logged <c>X-Correlation-ID</c> header, with the path it called;</item>
/// <item><c>run</c>: every trigger and action of a Logic App run whose tracked properties recorded it (Logic Apps
/// Standard telemetry v2, where a run's rows share its run ID, kept with its workflow's name in the JSON of their
/// <c>resource</c> property);</item>
/// <item><c>record</c>: a trace with tracking keys, such as an application's log in its logging scope or API
/// Management's <c>trace</c> policy, which belongs to the hop above it;</item>
/// <item><c>link</c>: a dependency's ID and parent, the steps between a hop and the hop it caused, with its time, name
/// and application, and whether it ran in process: a span an application or a framework started, such as the Partner
/// portal's for sending a booking, which records written outside any call belong to.</item>
/// </list>
/// Only keys are read, never a column that could hold a body, and of a request's URL only its path, never its query.
/// The time range is the query's own, set on the request.
/// </summary>
internal static class HopQuery
{
    public static string For(string correlationId) =>
        LogsQueryClient.CreateQuery($"let searched = {correlationId};") + "\n" + Body;

    private static readonly string Body = $$"""
        // The term index narrows each table to the rows mentioning the correlation ID before any JSON is parsed.
        let requests = AppRequests
            | where Properties has searched
            | project TimeGenerated, source = "request", Id, ParentId, OperationId, Name, path = tostring(parse_url(Url).Path), DurationMs, AppRoleName, Properties;
        // An application's own records, not those a framework wrote in its logging scope.
        let traces = AppTraces
            | where Properties has searched
            // The console's own audit events are read by AuditQuery, not shown as hops.
            | where isempty(tostring(Properties.Actor)) or isempty(tostring(Properties.Action))
            | where {{FrameworkLogs.NoneWrote("tostring(Properties.CategoryName)")}}
            | project TimeGenerated, source = "record", Id = "", ParentId, OperationId, Name = "", path = "", DurationMs = real(null), AppRoleName, Properties;
        let tracked = union requests, traces
            | extend tracked = parse_json(tostring(Properties.trackedProperties))
            | extend correlationId = coalesce(tostring(Properties.correlationId), tostring(tracked.correlationId),
                    tostring(Properties["Response-X-Correlation-ID"]), tostring(Properties["Request-X-Correlation-ID"])),
                runId = tostring(parse_json(tostring(Properties.resource)).runId)
            | where correlationId =~ searched;
        let runIds = toscalar(tracked | where isnotempty(runId) | summarize make_set(runId));
        // Every row of those runs, the actions without tracked properties included, so a run's hop lasts as long as it did.
        let runs = AppRequests
            | where OperationId in (runIds)
            | extend resource = parse_json(tostring(Properties.resource))
            | extend runId = tostring(resource.runId)
            | where runId in (runIds)
            | extend tracked = parse_json(tostring(Properties.trackedProperties))
            | project rowKind = "run", id = Id, parentId = ParentId, start = TimeGenerated, durationMs = DurationMs, name = Name,
                role = AppRoleName, runId, workflow = tostring(resource.workflowName), category = tostring(Properties.Category),
                flow = tostring(tracked.flow), outcome = tostring(tracked.outcome), failureCode = tostring(tracked.failureCode),
                transportOrderNumber = tostring(tracked.transportOrderNumber), shipmentNumber = tostring(tracked.shipmentNumber),
                carrierBookingNumber = tostring(tracked.carrierBookingNumber), tripNumber = tostring(tracked.tripNumber),
                sourceMessageId = tostring(tracked.sourceMessageId), messageId = tostring(tracked.messageId), OperationId;
        let calls = tracked
            | where isempty(runId)
            | project rowKind = source, id = Id, parentId = ParentId, start = TimeGenerated, durationMs = DurationMs, name = Name, path,
                role = AppRoleName, flow = coalesce(tostring(Properties.flow), tostring(tracked.flow)),
                outcome = coalesce(tostring(Properties.outcome), tostring(tracked.outcome)),
                failureCode = coalesce(tostring(Properties.failureCode), tostring(tracked.failureCode)),
                fault = tostring(Properties.fault),
                agentId = coalesce(tostring(Properties.agentId), tostring(tracked.agentId)),
                toolName = coalesce(tostring(Properties.toolName), tostring(tracked.toolName)),
                eventType = tostring(Properties.eventType),
                transportOrderNumber = coalesce(tostring(Properties.transportOrderNumber), tostring(tracked.transportOrderNumber)),
                shipmentNumber = coalesce(tostring(Properties.shipmentNumber), tostring(tracked.shipmentNumber)),
                carrierBookingNumber = coalesce(tostring(Properties.carrierBookingNumber), tostring(tracked.carrierBookingNumber)),
                tripNumber = coalesce(tostring(Properties.tripNumber), tostring(tracked.tripNumber)),
                sourceMessageId = coalesce(tostring(Properties.sourceMessageId), tostring(tracked.sourceMessageId)),
                messageId = coalesce(tostring(Properties.messageId), tostring(tracked.messageId)), OperationId;
        let operations = toscalar(union (calls | project OperationId), (runs | project OperationId) | summarize make_set(OperationId));
        let links = AppDependencies
            | where OperationId in (operations)
            | project rowKind = "link", id = Id, parentId = ParentId, start = TimeGenerated, durationMs = DurationMs, name = Name,
                role = AppRoleName, inProcess = DependencyType == "InProc";
        union (calls | project-away OperationId), (runs | project-away OperationId), links
        """;
}

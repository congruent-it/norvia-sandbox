using Azure;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Trails;

/// <summary>
/// A trail's hops from the workspace: the query's rows (calls, Logic App run rows, traces with tracking keys, and the
/// dependencies between them) put together into hops, each caused by the nearest hop up its parents.
/// </summary>
public sealed class LogAnalyticsTrailSourceHopsTests
{
    private const string WorkspaceId = "7f4c1b0e-0000-0000-0000-000000000001";
    private const string CorrelationId = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly LogsTableColumn[] Columns =
    [
        LogsQueryModelFactory.LogsTableColumn("rowKind", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("id", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("parentId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("start", LogsColumnType.Datetime),
        LogsQueryModelFactory.LogsTableColumn("durationMs", LogsColumnType.Real),
        LogsQueryModelFactory.LogsTableColumn("name", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("path", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("role", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("runId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("workflow", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("category", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("flow", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("outcome", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("failureCode", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("eventType", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("transportOrderNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("shipmentNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("carrierBookingNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("tripNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("sourceMessageId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("inProcess", LogsColumnType.Bool),
        LogsQueryModelFactory.LogsTableColumn("messageId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("fault", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("agentId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("toolName", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("mcpMethod", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("mcpTool", LogsColumnType.String),
    ];

    private static readonly string[] FrameworkCategories = ["Azure.", "Microsoft.", "System."];

    private readonly LogsQueryClient _client = Substitute.For<LogsQueryClient>();

    public LogAnalyticsTrailSourceHopsTests() => Answer();

    [Fact]
    public async Task HopsAsync_ForACorrelationId_QueriesTheWorkspace_ForItAsAnEscapedLiteral_InTheTimeRange()
    {
        const string correlationId = "x\" or true //";

        await HopsAsync(correlationId);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => query.Contains(LogsQueryClient.CreateQuery($"{correlationId}"), StringComparison.Ordinal) && !query.Contains(correlationId, StringComparison.Ordinal)),
            Arg.Is<LogsQueryTimeRange>(range => range.Start == Now.AddDays(-90) && range.End == Now),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HopsAsync_ForABooking_ReturnsTheApiManagementCall_TheIntakeRun_AndTheTmsCall_EachCausedByTheOneBefore()
    {
        Answer(
            // API Management's request, its trace policy's record, and its call to the Logic App.
            Row("request", "apim-req", null, Now.AddSeconds(-10), 1_500, "Submit transport order", "apim-norvia", flow: "transport-order-intake",
                path: "/partner/v1/transport-orders"),
            Row("record", null, "apim-req", Now.AddSeconds(-10), null, null, "apim-norvia", outcome: "Succeeded"),
            Row("link", "apim-dep", "apim-req"),
            // The intake run: its trigger, its call to the TMS with its tracked properties, and the answer.
            Row("run", "trigger", "apim-dep", Now.AddSeconds(-9.9), 10, "When_a_Shipper_books", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Triggers", flow: "transport-order-intake"),
            Row("run", "submit", "trigger", Now.AddSeconds(-9.5), 500, "Submit_to_the_TMS", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Actions", flow: "transport-order-intake", outcome: "Succeeded", transportOrderNumber: "TO-0000042"),
            Row("run", "answer", "trigger", Now.AddSeconds(-8.9), 100, "Answer_the_Shipper", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Actions"),
            Row("link", "submit-dep", "submit"),
            // The TMS's request, and its log record written inside a database call.
            Row("request", "tms-req", "submit-dep", Now.AddSeconds(-9.4), 300, "POST /v{version:apiVersion}/transport-orders/", "tms-api", flow: "transport-order-intake",
                path: "/v1/transport-orders"),
            Row("link", "tms-db", "tms-req"),
            Row("record", null, "tms-db", Now.AddSeconds(-9.3), null, null, "tms-api", transportOrderNumber: "TO-0000042"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().BeEquivalentTo(
        [
            new RecordedHop("apim-req", null, "apim-norvia: Submit transport order", "transport-order-intake", Now.AddSeconds(-10),
                TimeSpan.FromMilliseconds(1_500), Outcome.Succeeded, null, []),
            new RecordedHop("trigger", "apim-req", "logic-intake: transport-order-intake", "transport-order-intake", Now.AddSeconds(-9.9),
                TimeSpan.FromMilliseconds(1_100), Outcome.Succeeded, null, [new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000042")],
                new WorkflowRun("logic-intake", "transport-order-intake", "0858")),
            new RecordedHop("tms-req", "trigger", "tms-api: POST /v1/transport-orders", "transport-order-intake", Now.AddSeconds(-9.4),
                TimeSpan.FromMilliseconds(300), null, null, [new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000042")]),
        ]);
    }

    [Fact]
    public async Task HopsAsync_APlannersAssignment_IsNamedByThePathCalled_NotByItsRouteTemplate()
    {
        // ASP.NET Core names a request by its route template; the path actually called is its Url's.
        Answer(Row("request", "tms-req", null, Now.AddSeconds(-10), 162, "POST /v{version:apiVersion}/transport-orders/{transportOrderNumber}/assign", "tms-api",
            flow: "transport-planning", outcome: "Succeeded", path: "/v1/transport-orders/TO-0000042/assign"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Name.Should().Be("tms-api: POST /v1/transport-orders/TO-0000042/assign");
    }

    [Fact]
    public async Task HopsAsync_QueriesTheRequestsPath_NotItsWholeUrl()
    {
        // A query string could carry more than keys.
        await HopsAsync(CorrelationId);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => query.Contains("path = tostring(parse_url(Url).Path)", StringComparison.Ordinal)),
            Arg.Any<LogsQueryTimeRange>(),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HopsAsync_AnEventTheOutboxPublished_IsAHopNamedAfterTheEvent_ThatSucceeded()
    {
        // The outbox logs a published event after its send, outside any request, in the event's application properties
        // and its outcome.
        Answer(Row("record", null, null, Now.AddSeconds(-5), null, null, "tms-api", flow: "transport-planning", outcome: "Succeeded",
            transportOrderNumber: "TO-0000042", eventType: "ShipmentPlanned"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Name = "tms-api: publish ShipmentPlanned",
            Flow = "transport-planning",
            Outcome = Outcome.Succeeded,
            Duration = (TimeSpan?)null,
        });
    }

    [Fact]
    public async Task HopsAsync_ABookingFromThePartnerPortal_IsAHopOfTheSpanItWasSentIn_FromItsStart_AndCausedTheApiManagementCall()
    {
        // Blazor's span for the form's submit event, the portal's own span for sending the booking, and HttpClient's call
        // to API Management; the portal logs Norvia's answer once it has it, after the TMS has taken the booking.
        Answer(
            Link("event", null, Now.AddSeconds(-12), 4_000, "Event onsubmit -> Microsoft.AspNetCore.Components.Forms.EditForm.HandleSubmitAsync", "partner-portal-web", inProcess: true),
            Link("send", "event", Now.AddSeconds(-11), 2_400, "Send booking", "partner-portal-web", inProcess: true),
            Link("http", "send", Now.AddSeconds(-10.9), 2_200, "POST /partner/v1/transport-orders", "partner-portal-web", inProcess: false),
            Row("request", "apim-req", "http", Now.AddSeconds(-10.8), 2_000, "Submit transport order", "apim-norvia", flow: "transport-order-intake"),
            Row("request", "tms-req", "apim-req", Now.AddSeconds(-10), 1_600, "POST /v{version:apiVersion}/transport-orders/", "tms-api", flow: "transport-order-intake",
                outcome: "Succeeded", path: "/v1/transport-orders"),
            Row("record", null, "send", Now.AddSeconds(-8.6), null, null, "partner-portal-web", flow: "transport-order-intake", outcome: "Succeeded",
                transportOrderNumber: "TO-0000016"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().BeEquivalentTo(
        [
            new
            {
                Id = "send", CausedBy = (string?)null, Name = "partner-portal-web: Send booking", Time = Now.AddSeconds(-11),
                Duration = (TimeSpan?)TimeSpan.FromMilliseconds(2_400), Outcome = (Outcome?)Outcome.Succeeded,
            },
            new
            {
                Id = "apim-req", CausedBy = (string?)"send", Name = "apim-norvia: Submit transport order", Time = Now.AddSeconds(-10.8),
                Duration = (TimeSpan?)TimeSpan.FromMilliseconds(2_000), Outcome = (Outcome?)null,
            },
            new
            {
                Id = "tms-req", CausedBy = (string?)"apim-req", Name = "tms-api: POST /v1/transport-orders", Time = Now.AddSeconds(-10),
                Duration = (TimeSpan?)TimeSpan.FromMilliseconds(1_600), Outcome = (Outcome?)Outcome.Succeeded,
            },
        ]);
    }

    [Fact]
    public async Task HopsAsync_ARatingLoggedDuringADatabaseCall_IsAHopOfTheRatingsSpan()
    {
        // The rating worker's span is in-process; the database call under it isn't.
        Answer(
            Link("rate", null, Now.AddSeconds(-3), 173, "Rate Transport Order", "tms-api", inProcess: true),
            Link("sql", "rate", Now.AddSeconds(-2.95), 20, "norvia", "tms-api", inProcess: false),
            Row("record", null, "sql", Now.AddSeconds(-2.94), null, null, "tms-api", flow: "transport-order-rating", transportOrderNumber: "TO-0000016"),
            Row("record", null, "rate", Now.AddSeconds(-2.85), null, null, "tms-api", flow: "transport-order-rating", outcome: "Succeeded"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = "rate",
            Name = "tms-api: Rate Transport Order",
            Time = Now.AddSeconds(-3),
            Duration = (TimeSpan?)TimeSpan.FromMilliseconds(173),
            Outcome = (Outcome?)Outcome.Succeeded,
            BusinessKeys = new[] { new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000016") },
        });
    }

    [Fact]
    public async Task HopsAsync_ADeadLetteredRun_CarriesTheMessageIdItRecorded()
    {
        Answer(
            Row("run", "trigger", null, Now.AddSeconds(-5), 100, "When a message is received", "logic-intake", runId: "0858", workflow: "carrier-booking",
                category: "Workflow.Operations.Triggers", flow: "carrier-booking", outcome: "DeadLettered", failureCode: "CarrierRefused", messageId: "m-7"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.MessageId.Should().Be("m-7");
    }

    [Fact]
    public async Task HopsAsync_AHopThatRecordedNoMessageId_HasNone()
    {
        Answer(Row("request", "tms-req", null, Now.AddSeconds(-5), 100, "POST /x", "tms-api", flow: "carrier-booking", outcome: "Succeeded"));

        (await HopsAsync(CorrelationId)).Should().ContainSingle().Which.MessageId.Should().BeNull();
    }

    [Fact]
    public async Task HopsAsync_QueriesTheMessageIdOfEveryTrackedRow()
    {
        await HopsAsync(CorrelationId);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => query.Contains("messageId = tostring(tracked.messageId)", StringComparison.Ordinal)
                && query.Contains("messageId = coalesce(tostring(Properties.messageId), tostring(tracked.messageId))", StringComparison.Ordinal)),
            Arg.Any<LogsQueryTimeRange>(),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HopsAsync_QueriesTheLinksWithWhatPlacesAHopInThem()
    {
        // In AppDependencies, Type is the table's name; what kind of dependency it is, InProc for a span, is DependencyType.
        await HopsAsync(CorrelationId);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => query.Contains("inProcess = DependencyType == \"InProc\"", StringComparison.Ordinal)),
            Arg.Any<LogsQueryTimeRange>(),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HopsAsync_AContainerAppsRoleName_IsTheAppsNameWithoutItsEnvironment()
    {
        // Container Apps' OpenTelemetry agent gives every app its environment's generated name as service.namespace,
        // which Application Insights puts before the role name, however the app sets its resource attributes.
        Answer(
            Link("send", null, Now.AddSeconds(-11), 2_400, "Send booking", "[appseowsgwet24y3e]/partner-portal-web", inProcess: true),
            Row("request", "tms-req", null, Now.AddSeconds(-10), 1_500, "POST /v{version:apiVersion}/transport-orders/", "[appseowsgwet24y3e]/tms-api",
                path: "/v1/transport-orders"),
            Row("record", null, "send", Now.AddSeconds(-8.6), null, null, "[appseowsgwet24y3e]/partner-portal-web", outcome: "Succeeded"),
            Row("record", null, null, Now.AddSeconds(-8), null, null, "[appseowsgwet24y3e]/tms-api", eventType: "TransportOrderAccepted"));

        var hops = await HopsAsync(CorrelationId);

        hops.Select(hop => hop.Name).Should().BeEquivalentTo(
            "partner-portal-web: Send booking", "tms-api: POST /v1/transport-orders", "tms-api: publish TransportOrderAccepted");
    }

    [Fact]
    public async Task HopsAsync_TheEstatesResources_AreNamedWithoutTheirTokenOrRegion_ButARunKeepsItsLogicAppsName()
    {
        // The estate names a resource after its purpose and uniqueString(resourceGroup().id); API Management adds its region.
        Answer(
            Row("request", "apim-req", null, Now.AddSeconds(-10), 1_500, "POST /partner/v1/transport-orders", "apim-norvia-eowsgwet24y3e West Europe",
                path: "/partner/v1/transport-orders"),
            Row("link", "apim-dep", "apim-req"),
            Row("run", "trigger", "apim-dep", Now.AddSeconds(-9.9), 10, "When_a_Shipper_books", "logic-intake-eowsgwet24y3e", runId: "0858",
                workflow: "transport-order-intake", category: "Workflow.Operations.Triggers"));

        var hops = await HopsAsync(CorrelationId);

        hops.Select(hop => hop.Name).Should().BeEquivalentTo("apim-norvia: POST /partner/v1/transport-orders", "logic-intake: transport-order-intake");
        hops.Single(hop => hop.Run is not null).Run.Should().Be(new WorkflowRun("logic-intake-eowsgwet24y3e", "transport-order-intake", "0858"));
    }

    [Fact]
    public async Task HopsAsync_AnEventTheOutboxPublished_IsAHopOfItsPublishSpan_CausedByTheWorkThatRaisedIt()
    {
        // The TMS's outbox publishes in a span that continues the trace of what raised the event: the intake call raised
        // TransportOrderAccepted, the rating worker's span TransportOrderRated. A publish span is a dependency.
        Answer(
            Row("request", "tms-req", null, Now.AddSeconds(-10), 1_600, "POST /v{version:apiVersion}/transport-orders/", "[appseowsgwet24y3e]/tms-api",
                flow: "transport-order-intake", outcome: "Succeeded", path: "/v1/transport-orders"),
            Link("pub-accepted", "tms-req", Now.AddSeconds(-6), 100, "publish TransportOrderAccepted", "[appseowsgwet24y3e]/tms-api", inProcess: false),
            Row("record", null, "pub-accepted", Now.AddSeconds(-5.95), null, null, "[appseowsgwet24y3e]/tms-api", flow: "transport-order-intake",
                outcome: "Succeeded", eventType: "TransportOrderAccepted"),
            Link("rate", null, Now.AddSeconds(-8), 300, "Rate Transport Order", "[appseowsgwet24y3e]/tms-api", inProcess: true),
            Row("record", null, "rate", Now.AddSeconds(-7.8), null, null, "[appseowsgwet24y3e]/tms-api", flow: "transport-order-rating", outcome: "Succeeded"),
            Link("pub-rated", "rate", Now.AddSeconds(-5.9), 100, "publish TransportOrderRated", "[appseowsgwet24y3e]/tms-api", inProcess: false),
            Row("record", null, "pub-rated", Now.AddSeconds(-5.85), null, null, "[appseowsgwet24y3e]/tms-api", flow: "transport-order-rating",
                outcome: "Succeeded", eventType: "TransportOrderRated"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().BeEquivalentTo(
        [
            new { Id = "tms-req", CausedBy = (string?)null, Name = "tms-api: POST /v1/transport-orders" },
            new { Id = "pub-accepted", CausedBy = (string?)"tms-req", Name = "tms-api: publish TransportOrderAccepted" },
            new { Id = "rate", CausedBy = (string?)null, Name = "tms-api: Rate Transport Order" },
            new { Id = "pub-rated", CausedBy = (string?)"rate", Name = "tms-api: publish TransportOrderRated" },
        ]);
        hops.Single(hop => hop.Id == "pub-accepted").Should().BeEquivalentTo(new
        {
            Time = Now.AddSeconds(-6),
            Duration = (TimeSpan?)TimeSpan.FromMilliseconds(100),
            Outcome = (Outcome?)Outcome.Succeeded,
        });
    }

    [Fact]
    public async Task HopsAsync_ARecordUnderNoCall_IsAHopOfItsOwn_AtItsTime()
    {
        // The TMS's rating worker rates outside any request.
        Answer(
            Row("record", null, "rate", Now.AddSeconds(-5), null, null, "tms-api", flow: "transport-order-rating", outcome: "Succeeded"),
            Row("record", null, null, Now.AddSeconds(-4), null, null, "tms-api", flow: "transport-order-intake"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().HaveCount(2).And.OnlyContain(hop => hop.Name == "tms-api" && hop.Duration == null && hop.CausedBy == null);
        hops.Select(hop => hop.Time).Should().BeEquivalentTo([Now.AddSeconds(-5), Now.AddSeconds(-4)]);
        hops.Select(hop => hop.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task HopsAsync_RecordsUnderOneParentOutsideAnyHop_AreOneHop_AtTheFirstOnesTime()
    {
        // A worker's own span is no call on the trail, yet everything logged under it is one thing that happened.
        Answer(
            Row("record", null, "rate", Now.AddSeconds(-4), null, null, "tms-api", flow: "transport-order-rating", outcome: "Succeeded"),
            Row("record", null, "rate", Now.AddSeconds(-5), null, null, "tms-api", flow: "transport-order-rating", transportOrderNumber: "TO-0000008"),
            Row("record", null, "rate", Now.AddSeconds(-3), null, null, "partner-portal-web", flow: "transport-order-intake"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().HaveCount(2);
        var tms = hops.Should().ContainSingle(hop => hop.Name == "tms-api").Subject;
        tms.Time.Should().Be(Now.AddSeconds(-5));
        tms.Outcome.Should().Be(Outcome.Succeeded);
        tms.BusinessKeys.Should().Equal(new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000008"));
    }

    [Fact]
    public async Task HopsAsync_ARatingThatFailed_HasTheOutcomeSucceeded_WithItsFailureCode()
    {
        // The TMS's rating worker rates outside any request, and Rating failed is its answer, not a failed flow (P2).
        Answer(
            Row("record", null, "rate", Now.AddSeconds(-2), null, null, "tms-api", flow: "transport-order-rating", transportOrderNumber: "TO-0000009"),
            Row("record", null, "rate", Now.AddSeconds(-1), null, null, "tms-api", flow: "transport-order-rating", outcome: "Succeeded",
                failureCode: "NoMatchingLane", transportOrderNumber: "TO-0000009"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Flow = "transport-order-rating", Outcome = Outcome.Succeeded, FailureCode = "NoMatchingLane" });
    }

    [Fact]
    public async Task HopsAsync_QueriesNoRecordAFrameworkWrote()
    {
        // The Azure SDK logs in the application's logging scope, so its records carry the correlation ID too.
        await HopsAsync(CorrelationId);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => FrameworkCategories.All(prefix => query.Contains($"CategoryName) startswith \"{prefix}\"", StringComparison.Ordinal))),
            Arg.Any<LogsQueryTimeRange>(),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HopsAsync_ARunThatFailedAnAttemptThenSucceeded_HasTheOutcomeItEndedWith()
    {
        // Rows come back in no particular order; the run's retried call to the TMS first failed, then got through.
        Answer(
            Row("run", "submit-retried", "trigger", Now.AddSeconds(-4), 500, "Submit_to_the_TMS", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Actions", outcome: "Succeeded"),
            Row("run", "trigger", null, Now.AddSeconds(-10), 10, "When_a_Shipper_books", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Triggers"),
            Row("run", "submit", "trigger", Now.AddSeconds(-9), 500, "Submit_to_the_TMS", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Actions", outcome: "Failed", failureCode: "TmsUnavailable"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Outcome = Outcome.Succeeded, FailureCode = (string?)null });
    }

    [Fact]
    public async Task HopsAsync_ACallAFaultCaused_NamesTheFault()
    {
        Answer(
            Row("request", "driver-app", null, Now.AddSeconds(-5), 40, "POST /v1/trips", "driver-app-api"),
            Row("record", "", "driver-app", Now.AddSeconds(-5), null, null, "driver-app-api", flow: "trip-dispatch", outcome: "Failed", failureCode: "DriverAppUnavailable",
                fault: "driver-app-unavailable"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Fault.Should().Be("driver-app-unavailable");
    }

    [Fact]
    public async Task HopsAsync_AToolCallAnAgentMade_NamesTheAgentAndTheTool_AndAHopNoAgentMadeNamesNone()
    {
        Answer(
            Row("request", "mcp", null, Now.AddSeconds(-5), 40, "POST /mcp", "agent-mcp", flow: "agent-mcp-integration-operations", outcome: "Succeeded", agentId: "integration-operations-agent", toolName: "get_message_trail"),
            Row("request", "tms", "mcp", Now.AddSeconds(-4), 40, "POST /v1/bookings", "tms-api", outcome: "Succeeded"));

        var hops = await HopsAsync(CorrelationId);

        hops.Select(hop => (hop.AgentId, hop.ToolName)).Should().Equal(("integration-operations-agent", "get_message_trail"), (null, null));
    }

    [Fact]
    public async Task HopsAsync_AHopNoFaultCaused_NamesNone()
    {
        Answer(Row("request", "tms", null, Now.AddSeconds(-5), 40, "POST /v1/bookings", "tms-api", outcome: "Succeeded"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Fault.Should().BeNull();
    }

    [Fact]
    public async Task HopsAsync_AFailedRun_HasThatOutcome_WithItsFailureCode()
    {
        Answer(
            Row("run", "trigger", null, Now.AddSeconds(-10), 10, "When_a_Shipper_books", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Triggers"),
            Row("run", "submit", "trigger", Now.AddSeconds(-9), 500, "Submit_to_the_TMS", "logic-intake", runId: "0858", workflow: "transport-order-intake",
                category: "Workflow.Operations.Actions", outcome: "Failed", failureCode: "TmsUnavailable"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Outcome = Outcome.Failed, FailureCode = "TmsUnavailable" });
    }

    [Fact]
    public async Task HopsAsync_WithoutAWorkspace_FailsSayingSo()
    {
        var open = () => HopsAsync(CorrelationId, workspaceId: null);

        (await open.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Estate:LogAnalyticsWorkspaceId*");
    }

    private Task<IReadOnlyList<RecordedHop>> HopsAsync(string correlationId, string? workspaceId = WorkspaceId) =>
        new LogAnalyticsTrailSource(_client, Options.Create(new TrailSourceOptions { LogAnalyticsWorkspaceId = workspaceId }))
            .HopsAsync(new TrailLookup(correlationId, Now.AddDays(-90), Now), TestContext.Current.CancellationToken);

    private void Answer(params LogsTableRow[] rows)
    {
        var table = LogsQueryModelFactory.LogsTable("PrimaryResult", Columns, rows);
        var result = LogsQueryModelFactory.LogsQueryResult(
            [table], error: BinaryData.FromString("null"), statistics: BinaryData.FromString("{}"), visualization: BinaryData.FromString("{}"));
        _client.QueryWorkspaceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LogsQueryTimeRange>(), Arg.Any<LogsQueryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(result, Substitute.For<Response>()));
    }

    [Fact]
    public async Task HopsAsync_NamesAnMcpMessageByItsMethod_AndAToolCallByItsTool_AndTheEventStreamByWhatItIs()
    {
        const string path = "/agents/health-check/mcp";
        Answer(
            Row("request", "initialize", null, Now.AddSeconds(-10), 1, "POST /agents/health-check/mcp", "apim-norvia-eowsgwet24y3e West Europe", path: path, mcpMethod: "initialize"),
            Row("request", "stream", null, Now.AddSeconds(-9.9), 39, "GET /agents/health-check/mcp", "apim-norvia-eowsgwet24y3e West Europe", path: path),
            Row("request", "list", null, Now.AddSeconds(-9.8), 1, "POST /agents/health-check/mcp", "apim-norvia-eowsgwet24y3e West Europe", path: path, mcpMethod: "tools/list"),
            Row("request", "call", null, Now.AddSeconds(-9.7), 448, "POST /agents/health-check/mcp", "apim-norvia-eowsgwet24y3e West Europe", path: path, mcpMethod: "tools/call", mcpTool: "run_checks"));

        var hops = await HopsAsync(CorrelationId);

        hops.Select(hop => hop.Name).Should().Equal(
            "apim-norvia: MCP initialize", "apim-norvia: MCP event stream", "apim-norvia: MCP tools/list", "apim-norvia: MCP tools/call run_checks");
    }

    [Fact]
    public async Task HopsAsync_LeavesOtherCallsToTheMcpServersPathsNamedAsTheyWere()
    {
        Answer(
            Row("request", "view", null, Now.AddSeconds(-9), 445, "GET /agent-views/health-check/checks", "apim-norvia-eowsgwet24y3e West Europe", path: "/agent-views/health-check/checks"),
            Row("request", "post", null, Now.AddSeconds(-8), 5, "POST /agents/health-check/mcp", "apim-norvia-eowsgwet24y3e West Europe", path: "/agents/health-check/mcp"));

        var hops = await HopsAsync(CorrelationId);

        hops.Select(hop => hop.Name).Should().Equal("apim-norvia: GET /agent-views/health-check/checks", "apim-norvia: POST /agents/health-check/mcp");
    }

    // A link carries only its ID and parent; the query leaves the rest empty, as a union does.
    private static LogsTableRow Row(string kind, string? id, string? parentId) =>
        Row(kind, id, parentId, null, null, null, null);

    private static LogsTableRow Row(
        string kind,
        string? id,
        string? parentId,
        DateTimeOffset? start,
        double? durationMs,
        string? name,
        string? role,
        string? runId = null,
        string? workflow = null,
        string? category = null,
        string? flow = null,
        string? outcome = null,
        string? failureCode = null,
        string? transportOrderNumber = null,
        string? path = null,
        string? eventType = null,
        string? messageId = null,
        string? fault = null,
        string? agentId = null,
        string? toolName = null,
        string? mcpMethod = null,
        string? mcpTool = null) =>
        LogsQueryModelFactory.LogsTableRow(Columns,
        [
            kind, id, parentId, start, durationMs, name, path, role, runId, workflow, category,
            flow, outcome, failureCode, eventType, transportOrderNumber, null, null, null, null, null, messageId, fault, agentId, toolName, mcpMethod, mcpTool,
        ]);

    // A dependency the query returns with what places a hop in it: an in-process one is a span an application or a
    // framework started, such as the Partner portal's for sending a booking.
    private static LogsTableRow Link(string id, string? parentId, DateTimeOffset start, double durationMs, string name, string role, bool inProcess) =>
        LogsQueryModelFactory.LogsTableRow(Columns,
        [
            "link", id, parentId, start, durationMs, name, null, role, null, null, null,
            null, null, null, null, null, null, null, null, null, inProcess, null, null, null, null, null, null,
        ]);
}

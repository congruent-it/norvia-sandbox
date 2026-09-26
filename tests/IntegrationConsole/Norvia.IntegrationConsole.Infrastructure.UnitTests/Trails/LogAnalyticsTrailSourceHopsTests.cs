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
        LogsQueryModelFactory.LogsTableColumn("role", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("runId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("workflow", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("category", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("flow", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("outcome", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("failureCode", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("transportOrderNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("shipmentNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("carrierBookingNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("tripNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("sourceMessageId", LogsColumnType.String),
    ];

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
            Row("request", "apim-req", null, Now.AddSeconds(-10), 1_500, "Submit transport order", "apim-norvia", flow: "transport-order-intake"),
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
            Row("request", "tms-req", "submit-dep", Now.AddSeconds(-9.4), 300, "POST /v1/transport-orders", "tms-api", flow: "transport-order-intake"),
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
    public async Task HopsAsync_ARecordUnderNoCall_IsAHopOfItsOwn_AtItsTime()
    {
        // The TMS's outbox publishes an event outside any request.
        Answer(
            Row("record", null, "outbox-send", Now.AddSeconds(-5), null, null, "tms-api", flow: "transport-order-intake", outcome: "Succeeded"),
            Row("record", null, null, Now.AddSeconds(-4), null, null, "tms-api", flow: "transport-order-intake"));

        var hops = await HopsAsync(CorrelationId);

        hops.Should().HaveCount(2).And.OnlyContain(hop => hop.Name == "tms-api" && hop.Duration == null && hop.CausedBy == null);
        hops.Select(hop => hop.Time).Should().BeEquivalentTo([Now.AddSeconds(-5), Now.AddSeconds(-4)]);
        hops.Select(hop => hop.Id).Should().OnlyHaveUniqueItems();
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
        string? transportOrderNumber = null) =>
        LogsQueryModelFactory.LogsTableRow(Columns,
        [
            kind, id, parentId, start, durationMs, name, role, runId, workflow, category,
            flow, outcome, failureCode, transportOrderNumber, null, null, null, null,
        ]);
}

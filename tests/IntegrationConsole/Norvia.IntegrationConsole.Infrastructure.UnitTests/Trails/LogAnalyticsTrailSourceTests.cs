using Azure;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Trails;

public sealed class LogAnalyticsTrailSourceTests
{
    private const string WorkspaceId = "7f4c1b0e-0000-0000-0000-000000000001";

    private static readonly DateTimeOffset From = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly LogsTableColumn[] Columns =
    [
        LogsQueryModelFactory.LogsTableColumn("correlationId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("firstSeen", LogsColumnType.Datetime),
        LogsQueryModelFactory.LogsTableColumn("lastSeen", LogsColumnType.Datetime),
        LogsQueryModelFactory.LogsTableColumn("flows", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("outcomes", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("transportOrderNumbers", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("shipmentNumbers", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("carrierBookingNumbers", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("tripNumbers", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("sourceMessageIds", LogsColumnType.String),
    ];

    // The Azure SDK's clients are built to be substituted: their methods are virtual, and the model factory makes results.
    private readonly LogsQueryClient _client = Substitute.For<LogsQueryClient>();

    public LogAnalyticsTrailSourceTests() => Answer();

    [Fact]
    public async Task SearchAsync_ForAKey_QueriesTheWorkspace_ForThatKey_InTheTimeRange()
    {
        await Source().SearchAsync(new TrailSearch("TO-2026-000123", From, To), TestContext.Current.CancellationToken);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => query.Contains(LogsQueryClient.CreateQuery($"{"TO-2026-000123"}"), StringComparison.Ordinal)),
            Arg.Is<LogsQueryTimeRange>(range => range.Start == From && range.End == To),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ForAKeyWithQuotes_PassesItAsAnEscapedLiteral_NeverAsQueryText()
    {
        const string key = "x\" or true //";

        await Source().SearchAsync(new TrailSearch(key, From, To), TestContext.Current.CancellationToken);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => query.Contains(LogsQueryClient.CreateQuery($"{key}"), StringComparison.Ordinal) && !query.Contains(key, StringComparison.Ordinal)),
            Arg.Any<LogsQueryTimeRange>(),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ForATrailFound_ReadsItsFlowsTimesOutcomesAndBusinessKeys()
    {
        Answer(Row(
            "corr-1",
            From.AddHours(1),
            From.AddHours(2),
            flows: """["transport-order-intake"]""",
            outcomes: """["Succeeded","Refused"]""",
            transportOrderNumbers: """["TO-2026-000123"]""",
            shipmentNumbers: """["SH-2026-000456","SH-2026-000457"]""",
            sourceMessageIds: """["evt-5c1d"]"""));

        var trails = await Source().SearchAsync(new TrailSearch("TO-2026-000123", From, To), TestContext.Current.CancellationToken);

        var trail = trails.Should().ContainSingle().Subject;
        trail.CorrelationId.Should().Be("corr-1");
        trail.Flows.Should().Equal("transport-order-intake");
        trail.FirstSeen.Should().Be(From.AddHours(1));
        trail.LastSeen.Should().Be(From.AddHours(2));
        trail.Outcomes.Should().Equal(Outcome.Succeeded, Outcome.Refused);
        trail.BusinessKeys.Should().Equal(
            new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-2026-000123"),
            new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-2026-000456"),
            new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-2026-000457"),
            new BusinessKey(BusinessKeyKind.SourceMessageId, "evt-5c1d"));
    }

    [Fact]
    public async Task SearchAsync_ForAnOutcomeOutsideTheTrackingContract_LeavesItOut()
    {
        Answer(Row("corr-1", From, To, outcomes: """["Succeeded","Exploded"]"""));

        var trails = await Source().SearchAsync(new TrailSearch("corr-1", From, To), TestContext.Current.CancellationToken);

        trails.Single().Outcomes.Should().Equal(Outcome.Succeeded);
    }

    [Fact]
    public async Task SearchAsync_WithoutAWorkspace_FailsSayingSo()
    {
        var search = () => Source(workspaceId: null).SearchAsync(new TrailSearch("corr-1", From, To), TestContext.Current.CancellationToken);

        (await search.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Estate:LogAnalyticsWorkspaceId*");
    }

    private LogAnalyticsTrailSource Source(string? workspaceId = WorkspaceId) =>
        new(_client, Options.Create(new TrailSourceOptions { LogAnalyticsWorkspaceId = workspaceId }));

    private void Answer(params LogsTableRow[] rows)
    {
        var table = LogsQueryModelFactory.LogsTable("PrimaryResult", Columns, rows);
        var result = LogsQueryModelFactory.LogsQueryResult(
            [table], error: BinaryData.FromString("null"), statistics: BinaryData.FromString("{}"), visualization: BinaryData.FromString("{}"));
        _client.QueryWorkspaceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LogsQueryTimeRange>(), Arg.Any<LogsQueryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(result, Substitute.For<Response>()));
    }

    private static LogsTableRow Row(
        string correlationId,
        DateTimeOffset firstSeen,
        DateTimeOffset lastSeen,
        string flows = "[]",
        string outcomes = "[]",
        string transportOrderNumbers = "[]",
        string shipmentNumbers = "[]",
        string carrierBookingNumbers = "[]",
        string tripNumbers = "[]",
        string sourceMessageIds = "[]") =>
        LogsQueryModelFactory.LogsTableRow(Columns,
        [
            correlationId, firstSeen, lastSeen, flows, outcomes,
            transportOrderNumbers, shipmentNumbers, carrierBookingNumbers, tripNumbers, sourceMessageIds,
        ]);
}

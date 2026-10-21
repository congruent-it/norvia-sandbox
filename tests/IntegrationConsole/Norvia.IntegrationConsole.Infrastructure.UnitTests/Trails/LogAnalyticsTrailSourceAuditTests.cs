using Azure;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Trails;

/// <summary>The audit events the console wrote for a trail's messages, from the workspace.</summary>
public sealed class LogAnalyticsTrailSourceAuditTests
{
    private const string WorkspaceId = "7f4c1b0e-0000-0000-0000-000000000001";
    private const string CorrelationId = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly LogsTableColumn[] Columns =
    [
        LogsQueryModelFactory.LogsTableColumn("at", LogsColumnType.Datetime),
        LogsQueryModelFactory.LogsTableColumn("actor", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("action", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("entity", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("messageId", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("reason", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("transportOrderNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("shipmentNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("carrierBookingNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("tripNumber", LogsColumnType.String),
        LogsQueryModelFactory.LogsTableColumn("sourceMessageId", LogsColumnType.String),
    ];

    private readonly LogsQueryClient _client = Substitute.For<LogsQueryClient>();

    [Fact]
    public async Task AuditAsync_ForACorrelationId_QueriesTheWorkspace_ForItAsAnEscapedLiteral_InTheTimeRange()
    {
        const string correlationId = "x\" or true //";
        Answer();

        await AuditAsync(correlationId);

        await _client.Received(1).QueryWorkspaceAsync(
            WorkspaceId,
            Arg.Is<string>(query => query.Contains(LogsQueryClient.CreateQuery($"{correlationId}"), StringComparison.Ordinal) && !query.Contains(correlationId, StringComparison.Ordinal)),
            Arg.Is<LogsQueryTimeRange>(range => range.Start == Now.AddDays(-90) && range.End == Now),
            Arg.Any<LogsQueryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditAsync_ReturnsEachEvent_WithWhoWhatWhichMessageWhenWhyAndTheKeys()
    {
        Answer(
            LogsQueryModelFactory.LogsTableRow(Columns, [Now.AddMinutes(-5), "ana@norvia.example", "discard", "carrier-tracking", "m-1", "Duplicate", null, "SH-0042", null, null, null]),
            LogsQueryModelFactory.LogsTableRow(Columns, [Now.AddMinutes(-4), "ben@norvia.example", "resubmit", "carrier-tracking", "m-2", "", null, null, null, null, null]));

        var audits = await AuditAsync(CorrelationId);

        audits.Should().BeEquivalentTo(
        [
            new RecordedAudit("ana@norvia.example", "discard", "carrier-tracking", "m-1", Now.AddMinutes(-5), "Duplicate", [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042")]),
            new RecordedAudit("ben@norvia.example", "resubmit", "carrier-tracking", "m-2", Now.AddMinutes(-4), null, []),
        ], options => options.WithStrictOrdering());
    }

    private Task<IReadOnlyList<RecordedAudit>> AuditAsync(string correlationId) =>
        new LogAnalyticsTrailSource(_client, Options.Create(new TrailSourceOptions { LogAnalyticsWorkspaceId = WorkspaceId }))
            .AuditAsync(new TrailLookup(correlationId, Now.AddDays(-90), Now), TestContext.Current.CancellationToken);

    private void Answer(params LogsTableRow[] rows)
    {
        var table = LogsQueryModelFactory.LogsTable("PrimaryResult", Columns, rows);
        var result = LogsQueryModelFactory.LogsQueryResult(
            [table], error: BinaryData.FromString("null"), statistics: BinaryData.FromString("{}"), visualization: BinaryData.FromString("{}"));
        _client.QueryWorkspaceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LogsQueryTimeRange>(), Arg.Any<LogsQueryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(result, Substitute.For<Response>()));
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Audit;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Audit;

/// <summary>An audit event goes out through the logging pipeline, which ServiceDefaults exports through OpenTelemetry.</summary>
public sealed class OpenTelemetryAuditLogTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeLogger<OpenTelemetryAuditLog> _logger = new();

    [Fact]
    public async Task WriteAsync_Always_WritesOneInformationRecord_WithWhoWhatWhichMessageAndWhen()
    {
        await new OpenTelemetryAuditLog(_logger).WriteAsync(Event(), TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.Message.Should().Contain("Iris Operator").And.Contain("resubmit").And.Contain("carrier-tracking").And.Contain("original-1")
            .And.Contain("ShipmentNumber SH-0042").And.Contain("2026-09-26T09:00:00.0000000+00:00");
        record.StructuredState.Should().Contain(state => state.Key == "Actor" && state.Value == "Iris Operator");
        record.StructuredState.Should().Contain(state => state.Key == "MessageId" && state.Value == "original-1");
    }

    [Fact]
    public async Task WriteAsync_ForAMessageWithKeys_PutsTheCorrelationIdAndBusinessKeysInTheScope_AsTheTrackingContractNamesThem()
    {
        await new OpenTelemetryAuditLog(_logger).WriteAsync(Event(), TestContext.Current.CancellationToken);

        var scope = _logger.LatestRecord.Scopes.Should().ContainSingle().Subject
            .Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object>>>().Subject.ToDictionary(pair => pair.Key, pair => pair.Value);
        scope.Should().BeEquivalentTo(new Dictionary<string, object> { ["correlationId"] = "corr-1", ["shipmentNumber"] = "SH-0042", ["tripNumber"] = "TR-7" });
    }

    [Fact]
    public async Task WriteAsync_ForAMessageWithoutKeys_LeavesThemOutOfTheScope_NeverEmpty()
    {
        await new OpenTelemetryAuditLog(_logger).WriteAsync(Event() with { CorrelationId = null, BusinessKeys = [] }, TestContext.Current.CancellationToken);

        _logger.LatestRecord.Scopes.Single().Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object>>>().Which.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ForADiscard_CarriesTheReason()
    {
        await new OpenTelemetryAuditLog(_logger).WriteAsync(Event() with { Action = AuditActions.Discard, Reason = "Duplicate of SH-0041" }, TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        record.Message.Should().Contain("discard").And.Contain("reason: Duplicate of SH-0041");
        record.StructuredState.Should().Contain(state => state.Key == "Reason" && state.Value == "Duplicate of SH-0041");
    }

    private static AuditEvent Event() => new(
        "Iris Operator",
        AuditActions.Resubmit,
        "carrier-tracking",
        "original-1",
        "corr-1",
        [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042"), new BusinessKey(BusinessKeyKind.TripNumber, "TR-7")],
        At);
}

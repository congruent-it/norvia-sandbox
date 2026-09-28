using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.Reassign;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class TrackReassignmentInLoggingScopeTests : IDisposable
{
    private static readonly ReassignShipmentCommand Reassign = new("SH-0000012", null, "key-r1", "corr-reassign");

    private static readonly ReassignShipmentResult Reassigned = new(
        "SH-0000012",
        "TO-0000007",
        ShipmentStatus.Planned,
        new CarriedByView(AssignedTo.OwnFleet, null),
        new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
        "SHP-0001",
        "corr-reassign");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackReassignmentInLoggingScope _behaviour;

    public TrackReassignmentInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackReassignmentInLoggingScope(_loggerFactory.CreateLogger<TrackReassignmentInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Reassigned_LogsTheOutcomeOnTheReassignmentsOwnTrail_WithTheTransportOrderNumber()
    {
        await _behaviour.Handle(Reassign, Answering(Reassigned), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("SH-0000012").And.Contain("CAR-0001").And.Contain("Own fleet");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-reassign",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_RepeatUnderANewCallsTrail_LogsItAsARepeatOnTheRepeatedReassignmentsTrail()
    {
        await _behaviour.Handle(Reassign with { CorrelationId = "corr-retry" }, Answering(Reassigned with { Repeat = true }), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("SH-0000012").And.Contain("repeat");
        outcome.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().Last()
            .Should().Contain(new KeyValuePair<string, object?>("correlationId", "corr-reassign"));
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode_OnTheReassignmentsOwnTrail()
    {
        var inactive = AssignmentRefused.Because<ReassignShipmentResult>(new AssignmentRefusal(
            AssignmentRefusalCode.InactiveCarrier, "Carrier CAR-0009 (Gesloten Vracht) is not active, so it can't be chosen."));

        await _behaviour.Handle(Reassign, Answering(inactive), CancellationToken.None);

        var refused = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        refused.Message.Should().Contain("InactiveCarrier").And.Contain("CAR-0009");
        ScopeKeysOf(refused).Should().Contain(new Dictionary<string, object?>
        {
            ["shipmentNumber"] = "SH-0000012",
            ["correlationId"] = "corr-reassign",
            ["outcome"] = "Refused",
            ["failureCode"] = "InactiveCarrier",
        });
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private static Dictionary<string, object?> ScopeKeysOf(FakeLogRecord log) =>
        log.Scopes
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private static MessageHandlerDelegate<ReassignShipmentCommand, Result<ReassignShipmentResult>> Answering(
        Result<ReassignShipmentResult> result) =>
        (_, _) => ValueTask.FromResult(result);
}

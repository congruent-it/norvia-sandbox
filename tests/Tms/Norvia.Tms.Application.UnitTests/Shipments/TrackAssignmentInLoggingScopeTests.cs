using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class TrackAssignmentInLoggingScopeTests : IDisposable
{
    private static readonly AssignTransportOrderCommand Assign = new("TO-0000007", "CAR-0001", "key-1", "corr-planning");

    private static readonly AssignTransportOrderResult Planned = new(
        "SH-0000012",
        "TO-0000007",
        ShipmentStatus.Planned,
        new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
        "SHP-0001",
        "corr-planning");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackAssignmentInLoggingScope _behaviour;

    public TrackAssignmentInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackAssignmentInLoggingScope(_loggerFactory.CreateLogger<TrackAssignmentInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Planned_LogsTheOutcomeOnTheAssignmentsOwnTrail()
    {
        await _behaviour.Handle(Assign, Answering(Planned), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("TO-0000007").And.Contain("SH-0000012").And.Contain("CAR-0001");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_PlannedOnOwnFleet_SaysOwnFleet()
    {
        var ownFleet = Planned with { CarriedBy = new CarriedByView(AssignedTo.OwnFleet, null) };

        await _behaviour.Handle(Assign with { CarrierId = null }, Answering(ownFleet), CancellationToken.None);

        _logs.Collector.GetSnapshot().Should().ContainSingle().Which.Message.Should().Contain("Own fleet");
    }

    [Fact]
    public async Task Handle_Repeat_LogsItAsARepeat()
    {
        await _behaviour.Handle(Assign, Answering(Planned with { Repeat = true }), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("TO-0000007").And.Contain("repeat");
        ScopeKeysOf(outcome).Should().Contain("correlationId", "corr-planning").And.Contain("outcome", "Succeeded");
    }

    [Fact]
    public async Task Handle_RepeatUnderANewCallsTrail_LogsTheOutcomeOnTheRepeatedAssignmentsTrail()
    {
        await _behaviour.Handle(Assign with { CorrelationId = "corr-retry" }, Answering(Planned with { Repeat = true }), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().Last()
            .Should().Contain(new KeyValuePair<string, object?>("correlationId", "corr-planning"));
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode_OnTheAssignmentsOwnTrail()
    {
        var alreadyPlanned = AssignmentRefused.Because(new AssignmentRefusal(
            AssignmentRefusalCode.TransportOrderAlreadyPlanned, "Transport Order TO-0000007 is Planned already, as Shipment SH-0000003."));

        await _behaviour.Handle(Assign, Answering(alreadyPlanned), CancellationToken.None);

        var refused = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        refused.Message.Should().Contain("TransportOrderAlreadyPlanned").And.Contain("Transport Order TO-0000007 is Planned already");
        ScopeKeysOf(refused).Should().Contain(new Dictionary<string, object?>
        {
            ["transportOrderNumber"] = "TO-0000007",
            ["correlationId"] = "corr-planning",
            ["outcome"] = "Refused",
            ["failureCode"] = "TransportOrderAlreadyPlanned",
        });
        ScopeKeysOf(refused).Should().NotContainKey("shipmentNumber");
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

    private static MessageHandlerDelegate<AssignTransportOrderCommand, Result<AssignTransportOrderResult>> Answering(
        Result<AssignTransportOrderResult> result) =>
        (_, _) => ValueTask.FromResult(result);
}

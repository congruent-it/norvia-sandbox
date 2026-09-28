using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class TrackReleaseInLoggingScopeTests : IDisposable
{
    private static readonly ReleaseShipmentCommand Release = new("SH-0000012", "key-rel1", "corr-release");

    private static readonly ReleaseShipmentResult Released = new(
        "SH-0000012",
        "TO-0000007",
        ShipmentStatus.Released,
        new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
        "SHP-0001",
        "corr-release");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackReleaseInLoggingScope _behaviour;

    public TrackReleaseInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackReleaseInLoggingScope(_loggerFactory.CreateLogger<TrackReleaseInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Released_LogsTheOutcomeOnTheReleasesOwnTrail_WithTheTransportOrderNumber()
    {
        await _behaviour.Handle(Release, Answering(Released), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("SH-0000012").And.Contain("TO-0000007").And.Contain("CAR-0001");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-release",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_ReleasedToOwnFleet_NamesOwnFleet()
    {
        await _behaviour.Handle(Release, Answering(Released with { CarriedBy = new CarriedByView(AssignedTo.OwnFleet, null) }), CancellationToken.None);

        _logs.Collector.GetSnapshot().Should().ContainSingle().Which.Message.Should().Contain("Own fleet");
    }

    [Fact]
    public async Task Handle_RepeatUnderANewCallsTrail_LogsItAsARepeatOnTheRepeatedReleasesTrail()
    {
        await _behaviour.Handle(Release with { CorrelationId = "corr-retry" }, Answering(Released with { Repeat = true }), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("SH-0000012").And.Contain("repeat");
        outcome.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().Last()
            .Should().Contain(new KeyValuePair<string, object?>("correlationId", "corr-release"));
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode_OnTheReleasesOwnTrail()
    {
        var notPlanned = AssignmentRefused.Because<ReleaseShipmentResult>(new AssignmentRefusal(
            AssignmentRefusalCode.ShipmentNotPlanned, "Shipment SH-0000012 is Released. Only a Planned Shipment can be released."));

        await _behaviour.Handle(Release, Answering(notPlanned), CancellationToken.None);

        var refused = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        refused.Message.Should().Contain("ShipmentNotPlanned").And.Contain("SH-0000012");
        ScopeKeysOf(refused).Should().Contain(new Dictionary<string, object?>
        {
            ["shipmentNumber"] = "SH-0000012",
            ["correlationId"] = "corr-release",
            ["outcome"] = "Refused",
            ["failureCode"] = "ShipmentNotPlanned",
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

    private static MessageHandlerDelegate<ReleaseShipmentCommand, Result<ReleaseShipmentResult>> Answering(
        Result<ReleaseShipmentResult> result) =>
        (_, _) => ValueTask.FromResult(result);
}

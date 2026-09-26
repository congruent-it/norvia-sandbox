using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class TrackReRateInLoggingScopeTests : IDisposable
{
    private static readonly RateTransportOrderCommand ReRate = new("TO-0000007", ReRateKey: "key-1");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackReRateInLoggingScope _behaviour;

    public TrackReRateInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackReRateInLoggingScope(_loggerFactory.CreateLogger<TrackReRateInLoggingScope>());
    }

    [Fact]
    public async Task Handle_ReRateRated_LogsTheOutcomeOnTheIntakesTrail()
    {
        await _behaviour.Handle(ReRate, Answering(new RateTransportOrderResult(RatingResult.Rated, null, "SHP-0001", "corr-intake")), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("TO-0000007").And.Contain("Rated");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-intake",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000007",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task Handle_ReRateFailsAgain_LogsTheFailureCode()
    {
        var failed = new RateTransportOrderResult(
            RatingResult.RatingFailed, new RatingFailureView("NoMatchingLane", "No Lane."), "SHP-0001", "corr-intake");

        await _behaviour.Handle(ReRate, Answering(failed), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Level.Should().Be(LogLevel.Warning);
        ScopeKeysOf(outcome).Should().Contain("failureCode", "NoMatchingLane");
    }

    [Fact]
    public async Task Handle_Repeat_LogsItAsARepeat()
    {
        await _behaviour.Handle(
            ReRate,
            Answering(new RateTransportOrderResult(RatingResult.Rated, null, "SHP-0001", "corr-intake", Repeat: true)),
            CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("TO-0000007").And.Contain("repeat");
        ScopeKeysOf(outcome).Should().Contain("correlationId", "corr-intake");
    }

    [Fact]
    public async Task Handle_ReRateRefused_LogsTheRefusalWithTheTransportOrderNumber()
    {
        await _behaviour.Handle(ReRate, Answering(Result.Conflict("Transport Order TO-0000007 is Rated.")), CancellationToken.None);

        var refused = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        refused.Message.Should().Contain("Transport Order TO-0000007 is Rated.");
        ScopeKeysOf(refused).Should().Contain("transportOrderNumber", "TO-0000007");
    }

    [Fact]
    public async Task Handle_FirstRating_LeavesTheTrackingToTheRatingWorker()
    {
        var result = await _behaviour.Handle(
            ReRate with { ReRateKey = null },
            Answering(new RateTransportOrderResult(RatingResult.Rated, null, "SHP-0001", "corr-intake")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _logs.Collector.GetSnapshot().Should().BeEmpty();
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

    private static MessageHandlerDelegate<RateTransportOrderCommand, Result<RateTransportOrderResult>> Answering(
        Result<RateTransportOrderResult> result) =>
        (_, _) => ValueTask.FromResult(result);
}

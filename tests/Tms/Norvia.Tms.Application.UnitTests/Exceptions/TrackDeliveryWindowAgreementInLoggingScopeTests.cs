using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class TrackDeliveryWindowAgreementInLoggingScopeTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly AgreeDeliveryWindowCommand Agree = new("EX-0000005", At, At.AddHours(4), "key-agree", "corr-agree");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackDeliveryWindowAgreementInLoggingScope _behaviour;

    public TrackDeliveryWindowAgreementInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackDeliveryWindowAgreementInLoggingScope(_loggerFactory.CreateLogger<TrackDeliveryWindowAgreementInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Agreed_LogsTheOutcomeOnTheAgreementsOwnTrail()
    {
        var agreed = new AgreeDeliveryWindowResult("EX-0000005", "SH-0000012", At, At.AddHours(4), "corr-agree");

        await _behaviour.Handle(Agree, (_, _) => ValueTask.FromResult<Result<AgreeDeliveryWindowResult>>(agreed), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("EX-0000005").And.Contain("SH-0000012");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-agree",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode_OnTheAgreementsOwnTrail()
    {
        var refused = ExceptionRefused.Because<AgreeDeliveryWindowResult>(new ExceptionRefusal(ExceptionRefusalCode.ShipmentNotInTransport, "Shipment SH-0000012 is Booked, not Collected or In transit."));

        await _behaviour.Handle(Agree, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain("EX-0000005").And.Contain("Booked");
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-agree",
            ["outcome"] = "Refused",
            ["failureCode"] = "ShipmentNotInTransport",
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
}

using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.CancelCarrierBooking;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class TrackCarrierBookingCancellationInLoggingScopeTests : IDisposable
{
        private static readonly CancelCarrierBookingCommand Cancel = new("EX-0000005", "No show", "key-cancel", "corr-cancel");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackCarrierBookingCancellationInLoggingScope _behaviour;

    public TrackCarrierBookingCancellationInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackCarrierBookingCancellationInLoggingScope(_loggerFactory.CreateLogger<TrackCarrierBookingCancellationInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Cancelled_LogsTheOutcomeOnTheCancellationsOwnTrail()
    {
        var cancelled = new CancelCarrierBookingResult("EX-0000005", "SH-0000012", "CB-0000042", "corr-cancel");

        await _behaviour.Handle(Cancel, (_, _) => ValueTask.FromResult<Result<CancelCarrierBookingResult>>(cancelled), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("EX-0000005").And.Contain("SH-0000012");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-cancel",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode_OnTheCancellationsOwnTrail()
    {
        var refused = ExceptionRefused.Because<CancelCarrierBookingResult>(new ExceptionRefusal(ExceptionRefusalCode.ShipmentAlreadyCollected, "Shipment SH-0000012 is Collected, not Booked."));

        await _behaviour.Handle(Cancel, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain("EX-0000005").And.Contain("Collected");
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-cancel",
            ["outcome"] = "Refused",
            ["failureCode"] = "ShipmentAlreadyCollected",
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

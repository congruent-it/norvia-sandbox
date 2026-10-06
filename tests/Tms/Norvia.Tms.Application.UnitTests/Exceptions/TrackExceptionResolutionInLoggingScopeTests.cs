using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.Resolve;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class TrackExceptionResolutionInLoggingScopeTests : IDisposable
{
    private static readonly ResolveExceptionCommand Resolve = new("EX-0000005", ExceptionOutcome.Redelivered, "Thursday", "corr-resolve");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackExceptionResolutionInLoggingScope _behaviour;

    public TrackExceptionResolutionInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackExceptionResolutionInLoggingScope(_loggerFactory.CreateLogger<TrackExceptionResolutionInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Resolved_LogsTheOutcomeOnTheResolutionsOwnTrail()
    {
        var resolved = new ResolveExceptionResult("EX-0000005", "SH-0000012", ExceptionStatus.Resolved, ExceptionOutcome.Redelivered, "corr-resolve");

        await _behaviour.Handle(Resolve, (_, _) => ValueTask.FromResult<Result<ResolveExceptionResult>>(resolved), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("EX-0000005").And.Contain("SH-0000012").And.Contain("Redelivered");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-resolve",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode_OnTheResolutionsOwnTrail()
    {
        var refused = ExceptionRefused.Because<ResolveExceptionResult>(new ExceptionRefusal(ExceptionRefusalCode.ExceptionNotOpen, "Exception EX-0000005 is Resolved, not Open."));

        await _behaviour.Handle(Resolve, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain("EX-0000005").And.Contain("not Open");
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-resolve",
            ["outcome"] = "Refused",
            ["failureCode"] = "ExceptionNotOpen",
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

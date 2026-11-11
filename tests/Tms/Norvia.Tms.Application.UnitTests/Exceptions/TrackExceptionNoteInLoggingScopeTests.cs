using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.AddNote;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class TrackExceptionNoteInLoggingScopeTests : IDisposable
{
    private static readonly AddExceptionNoteCommand Add = new("EX-0000005", "Thursday", "Anna", "corr-note");

    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TrackExceptionNoteInLoggingScope _behaviour;

    public TrackExceptionNoteInLoggingScopeTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _behaviour = new TrackExceptionNoteInLoggingScope(_loggerFactory.CreateLogger<TrackExceptionNoteInLoggingScope>());
    }

    [Fact]
    public async Task Handle_Added_LogsTheOutcomeOnTheNotesOwnTrail()
    {
        var added = new AddExceptionNoteResult("EX-0000005", "SH-0000012", 1, "corr-note");

        await _behaviour.Handle(Add, (_, _) => ValueTask.FromResult<Result<AddExceptionNoteResult>>(added), CancellationToken.None);

        var outcome = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        outcome.Message.Should().Contain("EX-0000005").And.Contain("SH-0000012");
        ScopeKeysOf(outcome).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-note",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_Refused_LogsTheRefusalWithItsFailureCode_OnTheNotesOwnTrail()
    {
        var refused = ExceptionRefused.Because<AddExceptionNoteResult>(new ExceptionRefusal(ExceptionRefusalCode.ExceptionNotOpen, "Exception EX-0000005 is Resolved, not Open."));

        await _behaviour.Handle(Add, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        var log = _logs.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Message.Should().Contain("EX-0000005").And.Contain("not Open");
        ScopeKeysOf(log).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-note",
            ["outcome"] = "Refused",
            ["failureCode"] = "ExceptionNotOpen",
        });
    }

    [Fact]
    public async Task Handle_NoteSavedFromADraft_NamesTheAgentAndItsRun()
    {
        var added = new AddExceptionNoteResult("EX-0000005", "SH-0000012", 1, "corr-note");

        await _behaviour.Handle(Add with { ActingOn = new Norvia.Agents.AgentRun("planner-exception-note", "run-7") }, (_, _) => ValueTask.FromResult<Result<AddExceptionNoteResult>>(added), CancellationToken.None);

        ScopeKeysOf(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain(new Dictionary<string, object?>
        {
            ["agentId"] = "planner-exception-note",
            ["agentRunId"] = "run-7",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task Handle_NoteTheyWroteThemselves_NamesNoAgent()
    {
        var added = new AddExceptionNoteResult("EX-0000005", "SH-0000012", 1, "corr-note");

        await _behaviour.Handle(Add, (_, _) => ValueTask.FromResult<Result<AddExceptionNoteResult>>(added), CancellationToken.None);

        ScopeKeysOf(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Keys.Should().NotContain(["agentId", "agentRunId"]);
    }

    [Fact]
    public async Task Handle_RefusedNoteFromADraft_StillNamesTheAgentAndItsRun()
    {
        var refused = ExceptionRefused.Because<AddExceptionNoteResult>(new ExceptionRefusal(ExceptionRefusalCode.ExceptionNotOpen, "Exception EX-0000005 is Resolved, not Open."));

        await _behaviour.Handle(Add with { ActingOn = new Norvia.Agents.AgentRun("planner-exception-note", "run-7") }, (_, _) => ValueTask.FromResult(refused), CancellationToken.None);

        ScopeKeysOf(_logs.Collector.GetSnapshot().Should().ContainSingle().Subject).Should().Contain("agentRunId", "run-7").And.Contain("outcome", "Refused");
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

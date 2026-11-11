using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Exceptions.AddNote;

/// <summary>
/// The Planner adds a free-text note to an open Exception (P7), recorded with the Planner and the time. A note publishes
/// nothing. The action starts in the TMS UI, so its correlation ID is the TMS's own.
/// </summary>
/// <param name="ActingOn">The Agent run whose draft the note was saved from, edited or not, which the trace then names. Null when the Planner wrote it themselves.</param>
public sealed record AddExceptionNoteCommand(string ExceptionNumber, string Text, string Planner, string CorrelationId, AgentRun? ActingOn = null)
    : ICommand<Result<AddExceptionNoteResult>>;

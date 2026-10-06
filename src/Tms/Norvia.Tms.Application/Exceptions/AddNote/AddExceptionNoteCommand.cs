using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Exceptions.AddNote;

/// <summary>
/// The Planner adds a free-text note to an open Exception (P7), recorded with the Planner and the time. A note publishes
/// nothing. The action starts in the TMS UI, so its correlation ID is the TMS's own.
/// </summary>
public sealed record AddExceptionNoteCommand(string ExceptionNumber, string Text, string Planner, string CorrelationId)
    : ICommand<Result<AddExceptionNoteResult>>;

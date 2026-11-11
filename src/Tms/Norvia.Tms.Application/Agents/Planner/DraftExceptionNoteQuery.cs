using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>Ask the Planner agent for an editable draft of an internal note on an open Exception. A Suggestion: the TMS saves nothing until the Planner adds the note.</summary>
public sealed record DraftExceptionNoteQuery(string ExceptionNumber) : IQuery<Result<Suggestion>>;

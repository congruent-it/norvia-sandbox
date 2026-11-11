using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Asks the Planner agent's note definition for a draft. Its one tool is the Exception's timeline, so the draft can state only what the TMS
/// shows. The draft arrives as the Suggestion's cause; the TMS neither saves it nor resolves anything.
/// </summary>
public sealed class DraftExceptionNoteHandler(IReadRepository<ExceptionCase> repository, IAgentClient agent)
    : IQueryHandler<DraftExceptionNoteQuery, Result<Suggestion>>
{
    public const string AgentName = "planner-exception-note";

    public async ValueTask<Result<Suggestion>> Handle(DraftExceptionNoteQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Only an open Exception takes a note, so only an open one gets a draft.
        if (!ExceptionNumber.TryParse(query.ExceptionNumber, out var number)
            || await repository.FirstOrDefaultAsync(new ExceptionByNumberSpec(number), cancellationToken) is not { Status: ExceptionStatus.Open } exception)
        {
            return Result.NotFound();
        }

        // The Exception's own correlation ID, so the Agent's tool call joins its Message trail.
        return await agent.AskAsync(
            new AgentRequest(AgentName, PlannerQuestions.DraftExceptionNote(number.Value), exception.CorrelationId.Value),
            cancellationToken);
    }
}

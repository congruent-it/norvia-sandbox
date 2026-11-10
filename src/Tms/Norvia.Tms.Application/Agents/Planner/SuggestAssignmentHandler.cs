using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Asks the Planner agent for a recommendation. The agent has one read tool, the assignment options, so it can choose only among options
/// the TMS computed and the Assignment rules allow; the Suggestion is shown, never executed.
/// </summary>
public sealed class SuggestAssignmentHandler(IReadRepository<TransportOrder> repository, IAgentClient agent)
    : IQueryHandler<SuggestAssignmentQuery, Result<Suggestion>>
{
    public const string AgentName = "planner";

    public async ValueTask<Result<Suggestion>> Handle(SuggestAssignmentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await repository.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { } order)
        {
            return Result.NotFound();
        }

        // The order's own correlation ID, so the Agent's tool call joins its Message trail.
        return await agent.AskAsync(
            new AgentRequest(AgentName, PlannerQuestions.SuggestAssignment(number.Value), order.CorrelationId.Value),
            cancellationToken);
    }
}

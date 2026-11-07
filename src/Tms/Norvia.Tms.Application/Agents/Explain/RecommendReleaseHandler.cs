using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Billing.Release;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Explain;

/// <summary>
/// Asks the Customer service agent for a release recommendation. The TMS decides eligibility, never the Agent: the same
/// <see cref="ReleaseEligibilityCheck"/> the agent's tool answers from runs here too, and a Release the Agent gives for an order that is
/// not eligible is turned into Needs a person with the blocking reasons, so no ineligible order is ever suggested for release.
/// </summary>
public sealed class RecommendReleaseHandler(IReadRepository<TransportOrder> repository, ReleaseEligibilityCheck eligibility, IAgentClient agent)
    : IQueryHandler<RecommendReleaseQuery, Result<Suggestion>>
{
    public async ValueTask<Result<Suggestion>> Handle(RecommendReleaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await repository.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { BillingState: not null } order)
        {
            return Result.NotFound();
        }

        // The order's own correlation ID, so the Agent's tool calls join its Message trail. The same question every time.
        var answer = await agent.AskAsync(
            new AgentRequest(ExplainRejectedBookingHandler.AgentName, CustomerServiceQuestions.RecommendRelease(number.Value), order.CorrelationId.Value),
            cancellationToken);
        if (!answer.IsSuccess || answer.Value.Action != SuggestedActionKind.Release)
        {
            return answer;
        }

        var decided = await eligibility.CheckAsync(order, cancellationToken);
        return decided.Eligible
            ? answer
            : Result.Success(answer.Value with
            {
                Action = SuggestedActionKind.NeedsAPerson,
                SuggestedAction = $"Do not release Transport Order {number.Value}: the TMS does not allow it now ({string.Join(", ", decided.BlockingReasons)}).",
            });
    }
}

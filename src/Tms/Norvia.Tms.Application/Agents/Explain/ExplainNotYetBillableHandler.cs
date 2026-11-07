using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Explain;

public sealed class ExplainNotYetBillableHandler(IReadRepository<TransportOrder> repository, IAgentClient agent)
    : IQueryHandler<ExplainNotYetBillableQuery, Result<Suggestion>>
{
    public async ValueTask<Result<Suggestion>> Handle(ExplainNotYetBillableQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await repository.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { } order)
        {
            return Result.NotFound();
        }

        // The order's own correlation ID, so the Agent's tool calls join its Message trail. The same question every time.
        return await agent.AskAsync(
            new AgentRequest(ExplainRejectedBookingHandler.AgentName, CustomerServiceQuestions.ExplainNotYetBillable(number.Value), order.CorrelationId.Value),
            cancellationToken);
    }
}

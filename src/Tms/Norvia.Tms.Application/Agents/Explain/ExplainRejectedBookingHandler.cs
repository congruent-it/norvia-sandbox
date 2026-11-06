using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Explain;

public sealed class ExplainRejectedBookingHandler(IReadRepository<TransportOrder> repository, IAgentClient agent)
    : IQueryHandler<ExplainRejectedBookingQuery, Result<Suggestion>>
{
    /// <summary>The Customer service agent's definition (agents/customer-service.agent.yaml).</summary>
    public const string AgentName = "customer-service";

    public async ValueTask<Result<Suggestion>> Handle(ExplainRejectedBookingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The booking's own correlation ID, so the Agent's tool calls join its Message trail.
        var booking = await repository.FirstOrDefaultAsync(new RejectedBookingByIdSpec(new TransportOrderId(query.Id)), cancellationToken);
        if (booking is null)
        {
            return Result.NotFound();
        }

        // The same question every time: Customer service asks nothing free-form of the Agent.
        return await agent.AskAsync(
            new AgentRequest(AgentName, CustomerServiceQuestions.ExplainRejectedBooking(query.Id), booking.CorrelationId.Value),
            cancellationToken);
    }
}

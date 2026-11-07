using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Reply;

/// <summary>
/// Asks the reply agent for a draft. The agent is a separate definition and identity with one tool, the Shipper view (ADR 0011), so it
/// cannot name the Carrier or an internal Exception. The draft arrives as the Suggestion's cause; the TMS neither sends nor stores it.
/// </summary>
public sealed class DraftShipperReplyHandler(IReadRepository<TransportOrder> repository, IAgentClient agent)
    : IQueryHandler<DraftShipperReplyQuery, Result<Suggestion>>
{
    public const string AgentName = "customer-service-reply";

    public async ValueTask<Result<Suggestion>> Handle(DraftShipperReplyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Enum.IsDefined(query.Question)
            || !TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await repository.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { } order)
        {
            return Result.NotFound();
        }

        // The order's own correlation ID, so the Agent's tool call joins its Message trail.
        return await agent.AskAsync(
            new AgentRequest(AgentName, CustomerServiceQuestions.DraftReply(number.Value, query.Question), order.CorrelationId.Value),
            cancellationToken);
    }
}

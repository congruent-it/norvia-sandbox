using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Asks the Planner notification agent for a draft. Its two tools are the template for the Exception's type and the Shipper's view of the
/// Transport Order (ADR 0011), so the draft cannot name the Carrier or an internal remark or Exception: nothing it can call returns one.
/// The draft arrives as the Suggestion's cause; the TMS neither sends nor stores it.
/// </summary>
public sealed class DraftShipperNotificationHandler(IReadRepository<ExceptionCase> repository, IAgentClient agent)
    : IQueryHandler<DraftShipperNotificationQuery, Result<Suggestion>>
{
    public const string AgentName = "planner-notification";

    public async ValueTask<Result<Suggestion>> Handle(DraftShipperNotificationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Only an open Exception can be notified about, so only an open one gets a draft.
        if (!ExceptionNumber.TryParse(query.ExceptionNumber, out var number)
            || await repository.FirstOrDefaultAsync(new ExceptionByNumberSpec(number), cancellationToken) is not { Status: ExceptionStatus.Open } exception)
        {
            return Result.NotFound();
        }

        // The Exception's own correlation ID, so the Agent's tool calls join its Message trail.
        return await agent.AskAsync(
            new AgentRequest(AgentName, PlannerQuestions.DraftShipperNotification(number.Value, exception.TransportOrderNumber.Value), exception.CorrelationId.Value),
            cancellationToken);
    }
}

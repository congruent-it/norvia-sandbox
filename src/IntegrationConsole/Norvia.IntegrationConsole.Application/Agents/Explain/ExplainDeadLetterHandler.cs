using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

public sealed class ExplainDeadLetterHandler(IAgentClient agent) : IQueryHandler<ExplainDeadLetterQuery, Result<Suggestion>>
{
    public async ValueTask<Result<Suggestion>> Handle(ExplainDeadLetterQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var entity = query.Entity?.Trim();
        if (string.IsNullOrEmpty(entity))
        {
            return Result.Invalid(new ValidationError(nameof(query.Entity), "Explain a dead-lettered message by its queue or subscription name."));
        }

        var correlationId = string.IsNullOrWhiteSpace(query.CorrelationId) ? Guid.NewGuid().ToString() : query.CorrelationId.Trim();
        return await agent.AskAsync(
            new AgentRequest(ExplainTrailHandler.AgentName, AgentQuestions.ExplainDeadLetter(entity, query.SequenceNumber), correlationId),
            cancellationToken);
    }
}

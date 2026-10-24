using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

public sealed class ExplainFailedRunHandler(IAgentClient agent) : IQueryHandler<ExplainFailedRunQuery, Result<Suggestion>>
{
    public async ValueTask<Result<Suggestion>> Handle(ExplainFailedRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var flow = query.Flow?.Trim();
        if (string.IsNullOrEmpty(flow))
        {
            return Result.Invalid(new ValidationError(nameof(query.Flow), "Explain a failed run by its flow."));
        }

        // A run that tracked no Message trail has none to join: the Agent's calls get a trail of their own.
        var correlationId = string.IsNullOrWhiteSpace(query.CorrelationId) ? Guid.NewGuid().ToString() : query.CorrelationId.Trim();
        return await agent.AskAsync(
            new AgentRequest(ExplainTrailHandler.AgentName, AgentQuestions.ExplainFailedRun(flow, query.Start), correlationId),
            cancellationToken);
    }
}

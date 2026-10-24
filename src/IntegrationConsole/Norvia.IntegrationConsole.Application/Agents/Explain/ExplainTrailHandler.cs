using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

public sealed class ExplainTrailHandler(IAgentClient agent) : IQueryHandler<ExplainTrailQuery, Result<Suggestion>>
{
    /// <summary>The Integration operations agent's definition (agents/integration-operations.agent.yaml).</summary>
    public const string AgentName = "integration-operations";

    public async ValueTask<Result<Suggestion>> Handle(ExplainTrailQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var correlationId = query.CorrelationId?.Trim();
        if (string.IsNullOrEmpty(correlationId))
        {
            return Result.Invalid(new ValidationError(nameof(query.CorrelationId), "Explain a Message trail by its correlation ID."));
        }

        // The same question every time: the operator asks nothing free-form of the Agent in this slice.
        return await agent.AskAsync(
            new AgentRequest(AgentName, AgentQuestions.ExplainTrail(correlationId), correlationId),
            cancellationToken);
    }
}

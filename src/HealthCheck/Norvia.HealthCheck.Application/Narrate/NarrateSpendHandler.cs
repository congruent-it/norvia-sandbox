using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;

namespace Norvia.HealthCheck.Application.Narrate;

public sealed class NarrateSpendHandler(IAgentClient agent) : IQueryHandler<NarrateSpendQuery, Result<Narration>>
{
    /// <summary>The FinOps agent's definition (agents/finops.agent.yaml).</summary>
    public const string AgentName = FinOpsQuestions.AgentName;

    public async ValueTask<Result<Narration>> Handle(NarrateSpendQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var correlationId = query.CorrelationId?.Trim();
        if (string.IsNullOrEmpty(correlationId))
        {
            return Result.Invalid(new ValidationError(nameof(query.CorrelationId), "Ask the FinOps agent with a correlation ID."));
        }

        // The same question every time: the operator asks nothing free-form of the Agent.
        return await agent.NarrateAsync(new AgentRequest(AgentName, FinOpsQuestions.Narrate, correlationId), cancellationToken);
    }
}

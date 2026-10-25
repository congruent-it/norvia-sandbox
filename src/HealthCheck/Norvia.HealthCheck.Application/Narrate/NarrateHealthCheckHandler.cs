using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;

namespace Norvia.HealthCheck.Application.Narrate;

public sealed class NarrateHealthCheckHandler(IAgentClient agent) : IQueryHandler<NarrateHealthCheckQuery, Result<Narration>>
{
    /// <summary>The Estate health-check agent's definition (agents/estate-health-check.agent.yaml).</summary>
    public const string AgentName = "estate-health-check";

    public async ValueTask<Result<Narration>> Handle(NarrateHealthCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var correlationId = query.CorrelationId?.Trim();
        if (string.IsNullOrEmpty(correlationId))
        {
            return Result.Invalid(new ValidationError(nameof(query.CorrelationId), "Narrate a health check by its correlation ID."));
        }

        // The same question every time: the operator asks nothing free-form of the Agent.
        return await agent.NarrateAsync(new AgentRequest(AgentName, HealthCheckQuestions.Narrate, correlationId), cancellationToken);
    }
}

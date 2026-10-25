using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;

namespace Norvia.HealthCheck.Application.Narrate;

public sealed class ExplainFindingHandler(IAgentClient agent) : IQueryHandler<ExplainFindingQuery, Result<NarratedItem>>
{
    public async ValueTask<Result<NarratedItem>> Handle(ExplainFindingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var correlationId = query.CorrelationId?.Trim();
        var findingId = query.FindingId?.Trim();
        if (string.IsNullOrEmpty(correlationId) || string.IsNullOrEmpty(findingId) || !Enum.IsDefined(query.Question))
        {
            return Result.Invalid(new ValidationError(nameof(query), "Ask about a Finding of a run, with one of the fixed questions."));
        }

        var question = query.Question == FindingQuestion.WhyIsThisAProblem ? HealthCheckQuestions.Why(findingId) : HealthCheckQuestions.WhatChanges(findingId);
        var answer = await agent.NarrateAsync(new AgentRequest(NarrateHealthCheckHandler.AgentName, question, correlationId), cancellationToken);
        if (!answer.IsSuccess)
        {
            return Result<NarratedItem>.Unavailable([.. answer.Errors]);
        }

        // The answer is about this Finding alone: anything else the Agent says is refused rather than shown.
        return answer.Value.Items is [{ } item] && string.Equals(item.Id, findingId, StringComparison.Ordinal)
            ? item
            : Result<NarratedItem>.Unavailable(AgentFailureCodes.AgentUnavailable);
    }
}

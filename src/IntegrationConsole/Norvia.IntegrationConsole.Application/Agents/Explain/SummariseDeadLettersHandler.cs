using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

public sealed class SummariseDeadLettersHandler(IAgentClient agent) : IQueryHandler<SummariseDeadLettersQuery, Result<Suggestion>>
{
    public async ValueTask<Result<Suggestion>> Handle(SummariseDeadLettersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The summary is about no one Message trail: the Agent's calls get a trail of their own.
        return await agent.AskAsync(
            new AgentRequest(ExplainTrailHandler.AgentName, AgentQuestions.WhatNeedsAttention, Guid.NewGuid().ToString()),
            cancellationToken);
    }
}

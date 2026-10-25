using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.HealthCheck.Application.Narrate;

/// <summary>The two fixed questions about one Finding. There is no free text.</summary>
public enum FindingQuestion
{
    WhyIsThisAProblem,
    WhatChangesIfIFixIt,
}

/// <summary>Ask the Estate health-check agent one of the fixed questions about one Finding of a run.</summary>
/// <param name="CorrelationId">The ID of the health check run, which joins the Agent's tool calls to its Message trail.</param>
/// <param name="FindingId">The Finding asked about, such as <c>F-DLQ-ALERT</c>.</param>
public sealed record ExplainFindingQuery(string CorrelationId, string FindingId, FindingQuestion Question) : IQuery<Result<NarratedItem>>;

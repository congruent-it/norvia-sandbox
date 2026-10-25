using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.HealthCheck.Application.Narrate;

/// <summary>Ask the Estate health-check agent to narrate a health check: it runs the checks through its tool and explains the Findings it reads.</summary>
/// <param name="CorrelationId">The ID of the health check run, which joins the Agent's tool calls to its Message trail.</param>
public sealed record NarrateHealthCheckQuery(string CorrelationId) : IQuery<Result<Narration>>;

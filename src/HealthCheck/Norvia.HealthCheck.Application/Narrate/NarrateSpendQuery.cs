using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.HealthCheck.Application.Narrate;

/// <summary>Ask the FinOps agent to explain the estate's spend: it reads the cost view and the rising-spend Findings through its tools and names the resource and the toggle behind a rise.</summary>
/// <param name="CorrelationId">The ID of the question, which joins the Agent's tool calls to its Message trail.</param>
public sealed record NarrateSpendQuery(string CorrelationId) : IQuery<Result<Narration>>;

using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

/// <summary>Explain a failed run: the Integration operations agent says why it failed, from the failed runs and, when there is one, the Logic App run.</summary>
/// <param name="Flow">The flow of the failed run, as the Failed runs panel lists it.</param>
/// <param name="Start">When the run started, which with the flow names it.</param>
/// <param name="CorrelationId">The Message trail the run tracked, or null when it tracked none.</param>
public sealed record ExplainFailedRunQuery(string Flow, DateTimeOffset Start, string? CorrelationId = null) : IQuery<Result<Suggestion>>;

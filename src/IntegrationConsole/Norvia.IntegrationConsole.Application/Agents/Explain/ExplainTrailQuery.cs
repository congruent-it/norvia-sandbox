using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

/// <summary>Explain a Message trail: the Integration operations agent says why it failed, with the evidence it read and a suggested action.</summary>
public sealed record ExplainTrailQuery(string CorrelationId) : IQuery<Result<Suggestion>>;

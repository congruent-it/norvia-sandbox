using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Explain;

/// <summary>Explain a rating exception: the Customer service agent says why Rating failed and what fixes it, citing what it read.</summary>
public sealed record ExplainRatingFailureQuery(string TransportOrderNumber) : IQuery<Result<Suggestion>>;

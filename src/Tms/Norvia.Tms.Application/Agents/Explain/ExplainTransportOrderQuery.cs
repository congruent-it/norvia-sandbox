using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Explain;

/// <summary>Explain a Transport Order: the Customer service agent summarises its life from rating to billing, citing what it read, and says what, if anything, blocks it.</summary>
public sealed record ExplainTransportOrderQuery(string TransportOrderNumber) : IQuery<Result<Suggestion>>;

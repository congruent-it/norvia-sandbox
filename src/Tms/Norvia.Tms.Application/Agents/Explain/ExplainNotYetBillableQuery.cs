using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Explain;

/// <summary>Explain a Transport Order that is not yet billable: the Customer service agent says what holds it back and which of that Customer service can act on.</summary>
public sealed record ExplainNotYetBillableQuery(string TransportOrderNumber) : IQuery<Result<Suggestion>>;

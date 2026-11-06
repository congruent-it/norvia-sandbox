using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Explain;

/// <summary>Explain a Rejected booking: the Customer service agent says why it was rejected, citing what it read, and what the Shipper should change.</summary>
public sealed record ExplainRejectedBookingQuery(Guid Id) : IQuery<Result<Suggestion>>;

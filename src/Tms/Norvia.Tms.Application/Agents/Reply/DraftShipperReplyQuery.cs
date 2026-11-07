using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;

namespace Norvia.Tms.Application.Agents.Reply;

/// <summary>Ask the Customer service reply agent for an editable draft reply to the Shipper's question about a Transport Order. A Suggestion: the TMS sends nothing and stores nothing.</summary>
public sealed record DraftShipperReplyQuery(string TransportOrderNumber, ShipperQuestion Question) : IQuery<Result<Suggestion>>;

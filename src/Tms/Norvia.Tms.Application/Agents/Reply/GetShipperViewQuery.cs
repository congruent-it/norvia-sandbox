using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Agents.Reply;

/// <summary>Reads what a Transport Order's Shipper sees of it, for the reply agent's one tool (ADR 0011).</summary>
public sealed record GetShipperViewQuery(string TransportOrderNumber) : IQuery<Result<ShipperView>>;

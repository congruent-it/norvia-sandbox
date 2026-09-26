using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Rates a Transport Order from its Shipper's Rate Card (P2): the one "rate Transport Order" operation. The rating
/// worker sends it for an order that is Not yet rated. Customer service sends it as a re-rate, under a key of its own,
/// for an order whose Rating failed, typically after its Rate Card was corrected; a re-rate of any other order is
/// refused as a conflict, unless it is the last re-rate sent again under its key, which answers as before. It returns
/// the rating result the Transport Order has afterwards, with the failure when Rating failed.
/// </summary>
/// <param name="ReRateKey">
/// Set only for Customer service's re-rate: the idempotency key that makes a retry of it a repeat. Null for the first
/// rating.
/// </param>
public sealed record RateTransportOrderCommand(string TransportOrderNumber, string? ReRateKey = null) : ICommand<Result<RateTransportOrderResult>>;

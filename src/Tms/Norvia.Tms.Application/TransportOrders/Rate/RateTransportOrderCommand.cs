using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Rates a Transport Order from its Shipper's Rate Card (P2): the one "rate Transport Order" operation. The rating
/// worker sends it for an order that is Not yet rated. Customer service sends it as a re-rate for an order whose Rating
/// failed, typically after its Rate Card was corrected; a re-rate of any other order is refused as a conflict. It
/// returns the rating result the Transport Order has afterwards, with the failure when Rating failed.
/// </summary>
/// <param name="ReRate">True for Customer service's re-rate of an order whose Rating failed.</param>
public sealed record RateTransportOrderCommand(string TransportOrderNumber, bool ReRate = false) : ICommand<Result<RateTransportOrderResult>>;

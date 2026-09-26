using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Customer service rates a Transport Order whose Rating failed again (P2), typically after its Rate Card was corrected.
/// It runs the same rating as the first attempt, with the Rate Card version valid on the collection date. A Transport
/// Order that isn't Rating failed is refused as a conflict. It returns the rating result the order has afterwards.
/// </summary>
/// <param name="CorrelationId">The re-rate's own Message trail, as a Customer service action starts one.</param>
public sealed record ReRateTransportOrderCommand(string TransportOrderNumber, string CorrelationId) : ICommand<Result<RateTransportOrderResult>>;

using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Rates a Transport Order that is Not yet rated from its Shipper's Rate Card (P2). The rating worker sends it; a re-rate
/// is <see cref="ReRateTransportOrderCommand"/>. It returns the rating result the Transport Order has afterwards, with
/// the failure when Rating failed.
/// </summary>
public sealed record RateTransportOrderCommand(string TransportOrderNumber) : ICommand<Result<RateTransportOrderResult>>;

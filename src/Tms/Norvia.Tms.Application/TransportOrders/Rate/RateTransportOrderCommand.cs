using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Rates a Transport Order that is Not yet rated from its Shipper's Rate Card (P2). The rating worker sends it, and a
/// re-rate will too. It returns the rating result the Transport Order has afterwards.
/// </summary>
public sealed record RateTransportOrderCommand(string TransportOrderNumber) : ICommand<Result<RatingResult>>;

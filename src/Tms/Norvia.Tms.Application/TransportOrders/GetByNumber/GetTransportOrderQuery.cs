using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.GetByNumber;

public sealed record GetTransportOrderQuery(string TransportOrderNumber) : IQuery<Result<TransportOrderView>>;

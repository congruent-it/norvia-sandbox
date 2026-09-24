using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.List;

public sealed record ListTransportOrdersQuery(TransportOrderStatus Status) : IQuery<IReadOnlyList<TransportOrderSummary>>;

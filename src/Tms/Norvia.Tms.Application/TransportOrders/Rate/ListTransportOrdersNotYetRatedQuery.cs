using Mediator;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>The Transport Orders waiting for Rating, oldest first, as the rating worker sweeps them.</summary>
public sealed record ListTransportOrdersNotYetRatedQuery(int BatchSize) : IQuery<IReadOnlyList<TransportOrderToRate>>;

using Mediator;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>The Transport Orders waiting for Rating, oldest first, as the rating worker sweeps them a batch at a time.</summary>
/// <param name="Skip">How many of the oldest to pass over: those the sweep already tried and couldn't rate.</param>
public sealed record ListTransportOrdersNotYetRatedQuery(int Skip, int BatchSize) : IQuery<IReadOnlyList<TransportOrderToRate>>;

using Mediator;
using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Application.Shippers.List;

/// <summary>The Shippers the TMS knows, by name: what the Transport Orders list filters by.</summary>
public sealed record ListShippersQuery : IQuery<IReadOnlyList<ShipperView>>;

using Mediator;

namespace Norvia.Tms.Application.TransportOrders.CountPerDay;

/// <summary>How many Transport Orders came in each UTC day of a window, both ends included. The estate health check reads its bill against it.</summary>
public sealed record CountTransportOrdersPerDayQuery(DateOnly FirstDay, DateOnly LastDay) : IQuery<IReadOnlyList<TransportOrderDayCount>>;

/// <summary>The Transport Orders received on one UTC day. A day with none has no entry.</summary>
public sealed record TransportOrderDayCount(DateOnly Date, int Count);

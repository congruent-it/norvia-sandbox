using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.CountPerDay;

public sealed class CountTransportOrdersPerDayHandler(IReadRepository<TransportOrder> repository)
    : IQueryHandler<CountTransportOrdersPerDayQuery, IReadOnlyList<TransportOrderDayCount>>
{
    public async ValueTask<IReadOnlyList<TransportOrderDayCount>> Handle(CountTransportOrdersPerDayQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var from = new DateTimeOffset(query.FirstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(query.LastDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var receivedAt = await repository.ListAsync(new TransportOrdersReceivedBetweenSpec(from, until), cancellationToken);

        return [.. receivedAt
            .GroupBy(time => DateOnly.FromDateTime(time.UtcDateTime))
            .OrderBy(day => day.Key)
            .Select(day => new TransportOrderDayCount(day.Key, day.Count()))];
    }
}

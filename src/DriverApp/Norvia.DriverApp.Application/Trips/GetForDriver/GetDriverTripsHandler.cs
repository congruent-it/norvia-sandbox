using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips.GetForDriver;

public sealed class GetDriverTripsHandler(IReadRepository<DriverTrip> trips) : IQueryHandler<GetDriverTripsQuery, IReadOnlyList<DriverTripView>>
{
    public async ValueTask<IReadOnlyList<DriverTripView>> Handle(GetDriverTripsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await trips.ListAsync(new TripsOfDriverSpecification(new DriverId(query.DriverId)), cancellationToken);
        return [.. rows.Select(row => row.ToView())];
    }
}

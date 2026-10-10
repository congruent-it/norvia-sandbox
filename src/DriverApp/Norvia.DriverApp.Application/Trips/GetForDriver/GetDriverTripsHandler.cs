using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips.GetForDriver;

public sealed class GetDriverTripsHandler(IReadRepository<DriverTrip> trips) : IQueryHandler<GetDriverTripsQuery, IReadOnlyList<DriverTripView>>
{
    public async ValueTask<IReadOnlyList<DriverTripView>> Handle(GetDriverTripsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var found = await trips.ListAsync(new TripsOfDriverSpecification(query.DriverId), cancellationToken);
        return [.. found.Select(trip => new DriverTripView(
            trip.Id.Value, trip.Version, trip.DriverId, trip.Details.DriverName, trip.Details.Day, trip.Details.PlannedStart,
            trip.Details.PlannedDrivingDuration, trip.Details.Vehicle, trip.Details.Stops))];
    }
}

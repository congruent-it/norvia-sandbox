using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>The planned driving duration of the Own fleet's Trips, as P11 stores them: a Draft uses up hours, and discarding it gives them back.</summary>
public sealed class TripPlannedDrivingHours(IReadRepository<Trip> trips) : IPlannedDrivingHours
{
    public async Task<IReadOnlyList<PlannedDriving>> ListAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken) =>
        [.. (await trips.ListAsync(new TripsPlannedBetweenSpec(first, last), cancellationToken)).Select(trip => trip.PlannedDriving).OfType<PlannedDriving>()];
}

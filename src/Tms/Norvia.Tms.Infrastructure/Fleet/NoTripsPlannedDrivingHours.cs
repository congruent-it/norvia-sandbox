using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>Stands in until P11 plans Trips: no Driver has any planned driving, so every Driving-hour budget is full.</summary>
internal sealed class NoTripsPlannedDrivingHours : IPlannedDrivingHours
{
    public Task<IReadOnlyList<PlannedDriving>> ListAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlannedDriving>>([]);
}

using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>
/// The planned driving duration of the Own fleet's Trips, Draft and Dispatched (P11 plans them). Until P11 exists no Trip
/// does, so every Driving-hour budget is full.
/// </summary>
public interface IPlannedDrivingHours
{
    /// <summary>The planned driving of every Driver's Trips on the days from <paramref name="first"/> to <paramref name="last"/>, both included.</summary>
    Task<IReadOnlyList<PlannedDriving>> ListAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken);
}

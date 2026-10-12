using Norvia.DriverApp.Application.Trips;

namespace Norvia.DriverApp.Web.Trips;

/// <summary>How My Trips lists a Driver's Trips: by day, earliest first, and within a day by planned start.</summary>
internal static class TripsByDay
{
    public static IReadOnlyList<IGrouping<DateOnly, DriverTripView>> Of(IEnumerable<DriverTripView> trips) =>
        [.. trips
            .OrderBy(trip => trip.Day).ThenBy(trip => trip.PlannedStart).ThenBy(trip => trip.TripNumber, StringComparer.Ordinal)
            .GroupBy(trip => trip.Day)];
}

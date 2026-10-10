using Ardalis.Specification;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

internal static class TripLookup
{
    /// <summary>The Trip with this number, or null when there is none or the text is no Trip number.</summary>
    public static async Task<Trip?> FindAsync(this IReadRepositoryBase<Trip> trips, string number, CancellationToken cancellationToken) =>
        TripNumber.TryParse(number, out var parsed) ? await trips.FirstOrDefaultAsync(new TripByNumberSpec(parsed), cancellationToken) : null;
}

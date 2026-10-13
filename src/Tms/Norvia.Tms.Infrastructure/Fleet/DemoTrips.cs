using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>What tells the Trip of P12's demo from one the Planner built: the start of its idempotency key.</summary>
internal static class DemoTrips
{
    internal const string KeyPrefix = "demo-trip-execution-";

    public static bool IsDemoTrip(Trip trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return trip.IdempotencyKey.Value.StartsWith(KeyPrefix, StringComparison.Ordinal);
    }
}

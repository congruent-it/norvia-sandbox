namespace Norvia.Tms.Domain.Trips;

public readonly record struct TripId(Guid Value)
{
    public static TripId New() => new(Guid.CreateVersion7());
}

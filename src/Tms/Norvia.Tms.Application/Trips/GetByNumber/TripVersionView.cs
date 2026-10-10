using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.GetByNumber;

/// <param name="Cause">The technical cause the Trip dispatch Logic App reported, while the version is Not delivered.</param>
public sealed record TripVersionView(int Version, TripDeliveryState State, string? Cause);

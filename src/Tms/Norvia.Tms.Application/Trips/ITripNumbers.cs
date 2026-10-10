using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>Hands out Trip numbers, each only once.</summary>
public interface ITripNumbers
{
    Task<TripNumber> NextAsync(CancellationToken cancellationToken);
}

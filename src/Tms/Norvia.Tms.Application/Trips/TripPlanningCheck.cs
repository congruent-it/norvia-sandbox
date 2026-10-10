using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>What <see cref="TripPlanningChecks"/> found: the availability rules of P10 the Trip breaks, and P11's own rules.</summary>
public sealed record TripPlanningCheck(IReadOnlyList<TripAvailabilityFailure> Failures, IReadOnlyList<TripRefusal> Refusals)
{
    public static readonly TripPlanningCheck Passed = new([], []);

    public bool IsOk => Failures.Count == 0 && Refusals.Count == 0;
}

using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>What <see cref="TripPlanningChecks"/> found: the availability and load rules of P10 the Trip breaks, P11's own rules, and the warnings that don't stop it.</summary>
public sealed record TripPlanningCheck(IReadOnlyList<TripAvailabilityFailure> Failures, IReadOnlyList<TripRefusal> Refusals, IReadOnlyList<TripWarning> Warnings)
{
    public static readonly TripPlanningCheck Passed = new([], [], []);

    public bool IsOk => Failures.Count == 0 && Refusals.Count == 0;
}

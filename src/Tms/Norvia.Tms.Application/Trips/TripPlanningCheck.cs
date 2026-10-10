using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>What <see cref="TripPlanningChecks"/> found: the availability and load rules of P10 the Trip breaks, P11's own rules, and the warnings that don't stop it.</summary>
public sealed record TripPlanningCheck(IReadOnlyList<TripAvailabilityFailure> Failures, IReadOnlyList<TripRefusal> Refusals, IReadOnlyList<TripWarning> Warnings)
{
    public static readonly TripPlanningCheck Passed = new([], [], []);

    /// <summary>The same check with these refusals first, such as a Shipment number that is no Shipment.</summary>
    public TripPlanningCheck WithRefusalsFirst(IReadOnlyList<TripRefusal> first) => this with { Refusals = [.. first, .. Refusals] };

    public bool IsOk => Failures.Count == 0 && Refusals.Count == 0;
}

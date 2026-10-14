namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>
/// The Planner's stage of the funnel: of the <see cref="Orders"/> bookings Norvia is expected to accept, how many it assigns, how
/// many of those it means to put on Own fleet where they fit and on a Carrier otherwise, and how many it then reassigns.
/// </summary>
public sealed record PlannerPreview(int Orders, int Assigned, int TryOwnFleet, int Carrier, int Reassigned);

namespace Norvia.Tms.Application.Fleet.Planning.OpenDays;

/// <param name="Today">The day it was in Norvia's time when the job ran.</param>
/// <param name="Opened">The days this run opened, in order; none when the horizon was complete already.</param>
/// <param name="Closed">How many days before today this run closed.</param>
public sealed record OpenPlanningDaysResult(DateOnly Today, IReadOnlyList<DateOnly> Opened, int Closed);

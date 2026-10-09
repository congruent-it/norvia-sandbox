namespace Norvia.Tms.Application.Fleet.Planning.ChangeRoster;

/// <param name="DaysRewritten">How many open days got a Shift from the new Roster, or lost theirs.</param>
public sealed record ChangeRosterResult(int DaysRewritten);

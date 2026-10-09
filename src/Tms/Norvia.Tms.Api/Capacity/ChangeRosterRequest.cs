namespace Norvia.Tms.Api.Capacity;

/// <param name="Weekdays">A weekday name to its Shift; a weekday left out is a day off.</param>
public sealed record ChangeRosterRequest(IReadOnlyDictionary<string, RosterShiftRequest?>? Weekdays);

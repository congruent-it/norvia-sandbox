namespace Norvia.Tms.Application.Fleet.Planning;

public sealed record RosterWeekdayView(DayOfWeek Weekday, TimeOnly Start, TimeOnly End);

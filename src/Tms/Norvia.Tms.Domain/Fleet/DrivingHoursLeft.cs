namespace Norvia.Tms.Domain.Fleet;

/// <summary>What is left of a Driver's Driving-hour budget today, this week and this fortnight, and how many extensions this week.</summary>
public sealed record DrivingHoursLeft(TimeSpan Today, TimeSpan Week, TimeSpan Fortnight, int ExtensionsLeft);

using System.ComponentModel.DataAnnotations;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>TMS settings for the limits of the Driving-hour budget (P10), in hours. The defaults follow a simplified EU 561/2006; a demo can make them tight.</summary>
public sealed class DrivingHourOptions
{
    public const string SectionName = "DrivingHours";

    /// <summary>The planned driving a day normally allows.</summary>
    [Range(typeof(decimal), "0.5", "24")]
    public decimal DailyHours { get; init; } = (decimal)DrivingHourLimits.Default.Daily.TotalHours;

    /// <summary>What a day may be extended to, using one of the week's extensions.</summary>
    [Range(typeof(decimal), "0.5", "24")]
    public decimal ExtendedDailyHours { get; init; } = (decimal)DrivingHourLimits.Default.ExtendedDaily.TotalHours;

    /// <summary>How many days in a fixed week (Monday to Sunday) may be extended.</summary>
    [Range(0, 7)]
    public int ExtensionsPerWeek { get; init; } = DrivingHourLimits.Default.ExtensionsPerWeek;

    [Range(typeof(decimal), "0.5", "168")]
    public decimal WeeklyHours { get; init; } = (decimal)DrivingHourLimits.Default.Weekly.TotalHours;

    /// <summary>The limit over this week and the previous one.</summary>
    [Range(typeof(decimal), "0.5", "336")]
    public decimal FortnightlyHours { get; init; } = (decimal)DrivingHourLimits.Default.Fortnightly.TotalHours;

    public bool IsConsistent => ToLimits().IsConsistent;

    public DrivingHourLimits ToLimits() => new(
        TimeSpan.FromHours((double)DailyHours),
        TimeSpan.FromHours((double)ExtendedDailyHours),
        ExtensionsPerWeek,
        TimeSpan.FromHours((double)WeeklyHours),
        TimeSpan.FromHours((double)FortnightlyHours));
}

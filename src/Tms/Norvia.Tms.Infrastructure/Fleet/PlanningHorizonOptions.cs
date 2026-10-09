using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>TMS settings for the Planning horizon and the job that keeps it open (P10).</summary>
public sealed class PlanningHorizonOptions
{
    public const string SectionName = "PlanningHorizon";

    /// <summary>How many days after today are open too: today and the next 14 by default.</summary>
    [Range(1, 60)]
    public int Days { get; init; } = 14;

    /// <summary>The time zone whose midnight starts a new day. The horizon job runs at 00:00 there.</summary>
    [Required]
    public string TimeZone { get; init; } = "Europe/Amsterdam";

    /// <summary>
    /// How often the job looks whether a new day has begun. It runs at the first look after 00:00, and when the TMS starts,
    /// so a TMS that was down at midnight catches up as soon as it is back.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "01:00:00")]
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromMinutes(1);
}

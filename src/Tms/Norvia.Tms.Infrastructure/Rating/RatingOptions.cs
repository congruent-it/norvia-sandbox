using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.Rating;

/// <summary>TMS settings for the rating worker.</summary>
public sealed class RatingOptions
{
    public const string SectionName = "Rating";

    /// <summary>
    /// How often the worker sweeps for Transport Orders still Not yet rated, such as one accepted just before a crash.
    /// The trigger after acceptance rates the others within moments, so this only bounds how long a missed one waits.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "01:00:00")]
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>How many Transport Orders one sweep rates at most; the next sweep, or trigger, takes the rest.</summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Whether an acceptance triggers the worker. Always on, except in the test host that shows the sweep alone
    /// rates a Transport Order whose trigger was missed.
    /// </summary>
    public bool TriggerOnAcceptance { get; init; } = true;
}

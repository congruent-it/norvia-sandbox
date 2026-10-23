using System.ComponentModel.DataAnnotations;

namespace Norvia.ServiceDefaults.Faults;

/// <summary>How long a fault may stay on. A fault is switched on for a duration and expires by itself, so none stays on by accident.</summary>
public sealed class FaultOptions
{
    public const string SectionName = "Faults";

    /// <summary>The longest a fault may be switched on for. A few minutes: a demo needs no more.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a fault stays on when the switch names no duration. Never more than <see cref="MaxDuration"/>.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(2);
}

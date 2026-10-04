using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.TrackingEvents;

/// <summary>TMS settings for Tracking events.</summary>
public sealed class TrackingEventOptions
{
    public const string SectionName = "TrackingEvents";

    /// <summary>
    /// How far in the future a Tracking event's time may be before the TMS refuses it, to allow for the reporter's clock
    /// running ahead (P5).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(5);
}

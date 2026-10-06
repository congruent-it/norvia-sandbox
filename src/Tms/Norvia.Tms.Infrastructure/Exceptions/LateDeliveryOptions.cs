using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.Exceptions;

/// <summary>TMS settings for Late delivery Exceptions (P7).</summary>
public sealed class LateDeliveryOptions
{
    public const string SectionName = "LateDelivery";

    /// <summary>How long after its delivery window's latest time a Shipment may go undelivered before the time-based check opens a Late delivery Exception.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "7.00:00:00")]
    public TimeSpan Grace { get; init; } = TimeSpan.Zero;
}

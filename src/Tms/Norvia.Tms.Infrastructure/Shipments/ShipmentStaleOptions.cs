using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.Shipments;

/// <summary>TMS settings for when a Shipment on its way is flagged Stale (P5).</summary>
public sealed class ShipmentStaleOptions
{
    public const string SectionName = "ShipmentStale";

    /// <summary>How long after its collection window's latest time a Booked Shipment may go uncollected before it is Stale.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "7.00:00:00")]
    public TimeSpan CollectionGrace { get; init; } = TimeSpan.FromHours(2);

    /// <summary>How long a Collected or In transit Shipment may go without a Tracking event before it is Stale.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]
    public TimeSpan SilenceLimit { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How long after its delivery window's latest time a Shipment may go undelivered before it is Stale.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "7.00:00:00")]
    public TimeSpan DeliveryGrace { get; init; } = TimeSpan.FromHours(2);
}

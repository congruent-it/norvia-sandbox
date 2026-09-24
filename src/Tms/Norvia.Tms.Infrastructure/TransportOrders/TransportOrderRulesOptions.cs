using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.TransportOrders;

/// <summary>TMS settings for the Transport Order rules.</summary>
public sealed class TransportOrderRulesOptions
{
    public const string SectionName = "TransportOrderRules";

    /// <summary>
    /// The heaviest a single Transport Order may be, all goods lines together. A sandbox default chosen to be
    /// believable (one tractor with trailer), not to follow any regulation.
    /// </summary>
    [Range(typeof(decimal), "1", "1000000")]
    public decimal MaxTotalWeightKg { get; init; } = 24_000m;
}

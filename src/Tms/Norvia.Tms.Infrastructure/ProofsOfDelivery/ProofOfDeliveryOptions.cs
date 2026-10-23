using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.ProofsOfDelivery;

/// <summary>TMS settings for Proofs of delivery.</summary>
public sealed class ProofOfDeliveryOptions
{
    public const string SectionName = "ProofsOfDelivery";

    /// <summary>
    /// How far in the future a Proof of delivery's delivery time may be before the TMS refuses it, to allow for the
    /// sender's clock running ahead (P6).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether <see cref="MissingAfter"/> is set. False leaves it unset: the Planner's list flags no Proof of delivery missing. Only the
    /// as-found estate does this (the toggle <c>proofMissingThresholdUnset</c>, docs/operations/as-found.md).
    /// </summary>
    public bool MissingThresholdSet { get; init; } = true;

    /// <summary>
    /// How long after a Shipment's Delivered Tracking event its Proof of delivery may stay away before the Planner's list
    /// flags it missing (P6).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "30.00:00:00")]
    public TimeSpan MissingAfter { get; init; } = TimeSpan.FromHours(48);
}

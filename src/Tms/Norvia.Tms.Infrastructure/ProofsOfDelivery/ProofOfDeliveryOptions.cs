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
}

using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The TMS has the threshold after which a Delivered Shipment's Proof of delivery is flagged missing.</summary>
public sealed class ProofMissingThresholdCheck : Check
{
    public const string CheckId = "PROOF-MISSING-THRESHOLD";

    public override string Id => CheckId;

    public override string Rule => "The TMS has the threshold after which a Delivered Shipment's Proof of delivery is flagged missing.";

    public override string Title => "The Proof of delivery missing threshold is unset";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "proofMissingThresholdUnset";

    public override string Fix =>
        "Set proofMissingThresholdUnset to false and provision the estate (ProofsOfDelivery__MissingThresholdSet).";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.BusinessSafetyNets.ProofMissingThresholdSet == false
            ? [new Evidence("TMS", "The Proof of delivery missing threshold is unset, so a Delivered Shipment without a Proof of delivery is never flagged.")]
            : [];
    }
}

using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The TMS's Proof of delivery files move to the cool tier after 30 days.</summary>
public sealed class ProofFilesHotTierCheck : Check
{
    public const string CheckId = "PROOF-HOT-TIER";

    public override string Id => CheckId;

    public override string Rule => "The TMS's Proof of delivery files move to the cool tier after 30 days.";

    public override string Title => "Proof of delivery files stay in the hot tier";

    public override Severity Severity => Severity.Low;

    public override string AsFoundToggle => "proofFilesStayHot";

    public override string Fix =>
        "Set proofFilesStayHot to false and run azd provision: the AppHost then builds the TMS storage's lifecycle rule.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Storage.ProofFilesMoveToCool == false
            ? [new Evidence("TMS proofs-of-delivery container", "Its files never leave the hot tier, so old Proofs of delivery cost as much as new ones.")]
            : [];
    }
}

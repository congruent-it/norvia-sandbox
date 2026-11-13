using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The TMS's Proof of delivery files are deleted after the retention period (7 years by default, <c>proofRetentionDays</c>).</summary>
public sealed class ProofFilesRetentionCheck : Check
{
    public const string CheckId = "PROOF-RETENTION";

    public override string Id => CheckId;

    public override string Rule => "The TMS's Proof of delivery files are deleted after the retention period, 7 years by default.";

    public override string Title => "Proof of delivery files are never deleted";

    public override Severity Severity => Severity.Low;

    public override string AsFoundToggle => "proofFilesNeverDeleted";

    public override string Fix =>
        "Set proofFilesNeverDeleted to false and run azd provision: the AppHost then builds the TMS storage's deletion rule after proofRetentionDays.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        // An account with no lifecycle rules at all is PROOF-HOT-TIER's finding, not a second one here: this Check finds the rules that
        // move files to cool but never delete them.
        return estate.Storage.ProofFilesDeleted == false && estate.Storage.ProofFilesMoveToCool != false
            ? [new Evidence("TMS proofs-of-delivery container", "Its files are kept for good, past the retention period, with no rule that deletes them.")]
            : [];
    }
}

using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The inbound Proof of delivery container deletes a blob after the estate's retention.</summary>
public sealed class InboundLifecycleRuleCheck : Check
{
    public const string CheckId = "LIFECYCLE-INBOUND";

    public override string Id => CheckId;

    public override string Rule => "The inbound Proof of delivery container deletes a blob after the estate's retention.";

    public override string Title => "The inbound container has no lifecycle rule";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "noInboundLifecycleRule";

    public override string Fix =>
        "Set noInboundLifecycleRule to false and provision the estate: infra/estate/inbound-storage.bicep then builds the rule.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Storage.InboundHasLifecycleRule == false
            ? [new Evidence("inbound Proof of delivery container", "It has no lifecycle rule, so a blob a failed message left behind is never deleted.")]
            : [];
    }
}

using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The agents' model deployment has a quota that stops a runaway bill.</summary>
public sealed class UnlimitedModelQuotaCheck : Check
{
    public const string CheckId = "MODEL-QUOTA";

    public override string Id => CheckId;

    public override string Rule => "The agents' model deployment has a quota that stops a runaway bill.";

    public override string Title => "The model has no quota";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "unlimitedModelQuota";

    public override string Fix =>
        "Set unlimitedModelQuota to false and provision the estate: foundry.bicep then deploys the model with its capacity limit.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Agents is { ModelQuotaUnlimited: true }
            ? [new Evidence("model deployment", "It has no quota, so a loop of tool calls is limited by nothing but the bill.")]
            : [];
    }
}

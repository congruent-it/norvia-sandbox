using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The Driver app's transit container deletes a file after the estate's retention.</summary>
public sealed class TransitLifecycleRuleCheck : Check
{
    public const string CheckId = "LIFECYCLE-TRANSIT";

    public override string Id => CheckId;

    public override string Rule => "The Driver app's transit container deletes a file after the estate's retention.";

    public override string Title => "The transit container has no lifecycle rule";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "noTransitLifecycleRule";

    public override string Fix =>
        "Set noTransitLifecycleRule to false and provision the estate: the Driver app's storage then gets its rule.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Storage.TransitHasLifecycleRule == false
            ? [new Evidence("Driver app transit container", "It has no lifecycle rule, so files that were refused or never fetched stay for good.")]
            : [];
    }
}

using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The TMS has the thresholds that flag a quiet Shipment Stale.</summary>
public sealed class StaleThresholdsCheck : Check
{
    public const string CheckId = "STALE-THRESHOLDS";

    public override string Id => CheckId;

    public override string Rule => "The TMS has the thresholds that flag a quiet Shipment Stale.";

    public override string Title => "The Stale thresholds are unset";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "staleThresholdsUnset";

    public override string Fix =>
        "Set staleThresholdsUnset to false and provision the estate: the TMS then gets its thresholds again (ShipmentStale__ThresholdsSet).";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.BusinessSafetyNets.StaleThresholdsSet == false
            ? [new Evidence("TMS", "The thresholds that flag a Shipment Stale are unset, so a quiet Shipment is never flagged.")]
            : [];
    }
}

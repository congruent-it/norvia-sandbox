using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The TMS's time-based check opens a Late delivery Exception once the delivery window has passed.</summary>
public sealed class LateDeliveryCheckCheck : Check
{
    public const string CheckId = "LATE-DELIVERY-CHECK";

    public override string Id => CheckId;

    public override string Rule => "The TMS's time-based check opens a Late delivery Exception once the delivery window has passed.";

    public override string Title => "The Late delivery check is off";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "lateDeliveryCheckOff";

    public override string Fix =>
        "Set lateDeliveryCheckOff to false and provision the estate (LateDelivery__CheckEnabled).";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.BusinessSafetyNets.LateDeliveryCheckOn == false
            ? [new Evidence("TMS", "The Late delivery check is off, so a passed delivery window opens no Exception.")]
            : [];
    }
}

using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The action group the alerts use has a receiver.</summary>
public sealed class AlertReceiverCheck : Check
{
    public const string CheckId = "ALERT-RECEIVER";

    public override string Id => CheckId;

    public override string Rule => "The action group the alerts use has a receiver.";

    public override string Title => "The alerts go to nobody";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "noAlertReceiver";

    public override string Fix =>
        "Set noAlertReceiver to false and ESTATE_ALERT_EMAIL to an address, then provision the estate: infra/estate/monitoring.bicep builds the group with its receiver.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.AlertReceivers == 0
            ? [new Evidence("action group", "The action group the alerts use has no receiver, so every alert fires and nobody is told.")]
            : [];
    }
}

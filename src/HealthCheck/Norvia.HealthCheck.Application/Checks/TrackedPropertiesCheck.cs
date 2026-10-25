using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Every Logic App workflow records its tracked properties.</summary>
public sealed class TrackedPropertiesCheck : Check
{
    public const string CheckId = "TRACKED-PROPERTIES";

    public override string Id => CheckId;

    public override string Rule => "Every Logic App workflow records its tracked properties.";

    public override string Title => "A Logic App records no tracked properties";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "logicAppsWithoutTrackedProperties";

    public override string Fix =>
        "Remove the flow from asFound.logicAppsWithoutTrackedProperties and deploy its Logic App again (infra/as-found.sh does it): stage-logic-app.sh then keeps the tracked properties.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. estate.Flows
                .Where(flow => flow.RecordsTrackedProperties == false)
                .OrderBy(flow => flow.Name, StringComparer.Ordinal)
                .Select(flow => new Evidence(flow.Name, $"The {flow.Name} workflow records no tracked properties: its trail can be found by correlation ID, not by business key.")),
        ];
    }
}

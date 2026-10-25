using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Every Logic App has an alert on its failed runs.</summary>
public sealed class FailedRunAlertCheck : Check
{
    public const string CheckId = "FAILED-RUN-ALERT";

    public override string Id => CheckId;

    public override string Rule => "Every Logic App has an alert on its failed runs.";

    public override string Title => "A Logic App has no failed-run alert";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "flowsWithoutFailedRunAlert";

    public override string Fix =>
        "Remove the flow from asFound.flowsWithoutFailedRunAlert and provision the estate: infra/estate/alerts.bicep then builds alert-failed-runs-<flow>.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. estate.Flows
                .Where(flow => !flow.HasFailedRunAlert)
                .OrderBy(flow => flow.Name, StringComparer.Ordinal)
                .Select(flow => new Evidence(flow.Name, $"No alert watches the failed runs of the {flow.Name} Logic App.")),
        ];
    }
}

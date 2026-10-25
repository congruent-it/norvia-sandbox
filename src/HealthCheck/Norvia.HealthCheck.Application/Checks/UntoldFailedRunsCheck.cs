using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>A Logic App failed and has no failed-run alert, so nobody was told.</summary>
public sealed class UntoldFailedRunsCheck : UntoldFailureCheck
{
    public const string CheckId = "FAILED-RUNS-UNTOLD";

    public override string Id => CheckId;

    public override string Rule => "A Logic App that failed has an alert on its failed runs, so somebody is told.";

    public override string Title => "A Logic App failed and nobody was told";

    public override string AsFoundToggle => "flowsWithoutFailedRunAlert";

    public override string Fix =>
        "Remove the flow from asFound.flowsWithoutFailedRunAlert and provision the estate: infra/estate/alerts.bicep then builds alert-failed-runs-<flow>. Then look at the failed runs in the Integration console.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. estate.Flows
                .Where(flow => !flow.HasFailedRunAlert && flow.FailedRuns > 0)
                .OrderBy(flow => flow.Name, StringComparer.Ordinal)
                .Select(flow => new Evidence(
                    flow.Name,
                    $"The {flow.Name} Logic App failed {flow.FailedRuns} {(flow.FailedRuns == 1 ? "time" : "times")} in the last {EstateConfiguration.FailureWindowDays} days and has no failed-run alert.")),
        ];
    }
}

using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The Driver reports Logic App tells the Driver app what became of each report.</summary>
public sealed class DriverReportBackCheck : Check
{
    public const string CheckId = "DRIVER-REPORT-BACK";

    public override string Id => CheckId;

    public override string Rule => "The Driver reports Logic App tells the Driver app what became of each report.";

    public override string Title => "The Driver reports Logic App does not report back";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "noReportBackToDriverApp";

    public override string Fix =>
        "Set noReportBackToDriverApp to false and deploy the Driver reports Logic App again (infra/as-found.ps1 does it).";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.DriverReportsReportBack == false
            ? [new Evidence("driver-reports", "The Logic App does not tell the Driver app what became of a report, so the report stays Sending although the TMS recorded it.")]
            : [];
    }
}

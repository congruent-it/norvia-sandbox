namespace Norvia.DriverApp.Domain.Reports;

/// <summary>A report's ID: the Driver app event ID, which names the report to the Driver reports Logic App, the TMS and the outcome report.</summary>
public readonly record struct ReportId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What the Driver reported (P12). The other reports of a Driver (a Stop, a Proof of delivery, the end of the Trip) join as they are built.</summary>
public enum ReportKind
{
    StartTrip,
}

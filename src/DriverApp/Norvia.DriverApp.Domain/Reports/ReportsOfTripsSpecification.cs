using Ardalis.Specification;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>The reports of the given Trips, oldest first: what the Trip screen shows a Driver of each.</summary>
public sealed class ReportsOfTripsSpecification : Specification<DriverReport, DriverReportRow>
{
    public ReportsOfTripsSpecification(IReadOnlyCollection<TripNumber> tripNumbers)
    {
        ArgumentNullException.ThrowIfNull(tripNumbers);
        Query.AsNoTracking()
            .Where(report => tripNumbers.Contains(report.TripNumber))
            .OrderBy(report => report.EventTime);
        Query.Select(report => new DriverReportRow(
            report.Id, report.Kind, report.TripNumber, report.ShipmentNumber, report.Stop, report.Remark, report.State, report.FailureCode, report.Message, report.EventTime, report.OutcomeAt));
    }
}

using Ardalis.Specification;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>A Start Trip of the Trip that the Driver can't send again: the TMS has it, or hasn't answered yet.</summary>
public sealed class ActiveStartOfTripSpecification : Specification<DriverReport>
{
    public ActiveStartOfTripSpecification(TripNumber tripNumber) =>
        Query.Where(report => report.TripNumber == tripNumber
            && report.Kind == ReportKind.StartTrip
            && (report.State == ReportState.Sending || report.State == ReportState.Recorded));
}

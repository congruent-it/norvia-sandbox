using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips.GetForDriver;

public sealed class GetDriverTripsHandler(IReadRepository<DriverTrip> trips, IReadRepository<DriverReport> reports, TimeProvider time)
    : IQueryHandler<GetDriverTripsQuery, IReadOnlyList<DriverTripView>>
{
    public async ValueTask<IReadOnlyList<DriverTripView>> Handle(GetDriverTripsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await trips.ListAsync(new TripsOfDriverSpecification(new DriverId(query.DriverId)), cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var reportsByTrip = (await reports.ListAsync(new ReportsOfTripsSpecification([.. rows.Select(row => row.TripNumber)]), cancellationToken))
            .ToLookup(report => report.TripNumber);
        var today = NorviaCalendar.Today(time.GetUtcNow());
        return [.. rows.Select(row => row.ToView([.. reportsByTrip[row.TripNumber]], today))];
    }
}

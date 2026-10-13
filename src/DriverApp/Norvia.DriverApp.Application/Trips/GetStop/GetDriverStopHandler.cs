using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips.GetStop;

public sealed class GetDriverStopHandler(IReadRepository<DriverTrip> trips, IReadRepository<DriverReport> reports)
    : IQueryHandler<GetDriverStopQuery, DriverStopView?>
{
    public async ValueTask<DriverStopView?> Handle(GetDriverStopQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tripNumber = new TripNumber(query.TripNumber);

        // A Driver sees only their own Trips, so another Driver's is as unknown as one that doesn't exist.
        if (await trips.GetByIdAsync(tripNumber, cancellationToken) is not { } trip || trip.DriverId != new DriverId(query.DriverId))
        {
            return null;
        }

        var held = await reports.ListAsync(new ReportsOfTripsSpecification([tripNumber]), cancellationToken);
        if (StopProgress.Of(trip.Details, held).FirstOrDefault(stop => stop.ShipmentNumber == query.ShipmentNumber && stop.Kind == query.Kind) is not { } found)
        {
            return null;
        }

        return new DriverStopView(trip.Id.Value, trip.Version, StopProgress.IsStarted(held) ? TripMapping.InProgressStatus : TripMapping.DispatchedStatus, trip.Details.Day, found);
    }
}

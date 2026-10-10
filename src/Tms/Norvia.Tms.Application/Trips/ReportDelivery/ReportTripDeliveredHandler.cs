using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

public sealed class ReportTripDeliveredHandler(IRepository<Trip> trips, TimeProvider time)
    : ICommandHandler<ReportTripDeliveredCommand, Result<TripDeliveryReportResult>>
{
    public async ValueTask<Result<TripDeliveryReportResult>> Handle(ReportTripDeliveredCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await TripDeliveryReports.HandleAsync(trips, command.Number, command.Version, trip => trip.ReportDelivered(command.Version, time.GetUtcNow()), cancellationToken);
    }
}

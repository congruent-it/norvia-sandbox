using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

public sealed class ReportTripNotDeliveredHandler(IRepository<Trip> trips, TimeProvider time)
    : ICommandHandler<ReportTripNotDeliveredCommand, Result<TripDeliveryReportResult>>
{
    public async ValueTask<Result<TripDeliveryReportResult>> Handle(ReportTripNotDeliveredCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One save writes the version's state and TripNotDelivered's outbox message together.
        return await TripDeliveryReports.HandleAsync(
            trips, command.Number, command.Version, trip => trip.ReportNotDelivered(command.Version, command.Cause, new CorrelationId(command.CorrelationId), time.GetUtcNow()), cancellationToken);
    }
}

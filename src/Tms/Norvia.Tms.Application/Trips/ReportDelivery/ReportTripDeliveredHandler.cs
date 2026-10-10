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

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<TripDeliveryReportResult>(TripRefusal.TripNotFound(command.Number));
        }

        var report = trip.ReportDelivered(command.Version, time.GetUtcNow());
        if (report == TripDeliveryReport.Recorded)
        {
            await trips.SaveChangesAsync(cancellationToken);
        }

        // Late or repeated: ignored, changing and publishing nothing.
        return TripDeliveryReports.Answer(trip, command.Version, report);
    }
}

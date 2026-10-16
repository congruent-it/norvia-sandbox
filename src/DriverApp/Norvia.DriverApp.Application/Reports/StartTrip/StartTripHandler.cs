using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Reports.StartTrip;

/// <summary>
/// Records the Driver's Start Trip and queues <c>TripStartReported</c> in the outbox. Both are saved in one transaction when the report is
/// added, so the event is published exactly once and only for a report that was recorded. The Driver app doesn't judge the day or the status:
/// the TMS decides, and its refusal comes back as the report's outcome.
/// </summary>
public sealed class StartTripHandler(
    IReadRepository<DriverTrip> trips,
    IRepository<DriverReport> reports,
    IDriverEventOutbox outbox,
    TimeProvider time) : ICommandHandler<StartTripCommand, Result<StartTripResult>>
{
    public async ValueTask<Result<StartTripResult>> Handle(StartTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tripNumber = new TripNumber(command.TripNumber);
        var driverId = new DriverId(command.DriverId);

        // A Driver sees only their own Trips, so another Driver's is as unknown as one that doesn't exist.
        if (await trips.GetByIdAsync(tripNumber, cancellationToken) is not { } trip || trip.DriverId != driverId)
        {
            return Result.NotFound();
        }

        if (await reports.FirstOrDefaultAsync(new ActiveStartOfTripSpecification(tripNumber), cancellationToken) is { } active)
        {
            return new StartTripResult(active.Id.Value, AlreadyReported: true);
        }

        var now = time.GetUtcNow();
        var report = DriverReport.StartTrip(new ReportId(Guid.CreateVersion7(now)), tripNumber, driverId, trip.Version, now, Guid.CreateVersion7(now).ToString());

        // Queued before the report is saved: the save writes the report and the outbox message together.
        outbox.Add(new TripStartReportedV1(report.Id.Value, report.EventTime, report.CorrelationId, tripNumber.Value, report.VersionHeld, driverId.Value));
        await reports.AddAsync(report, cancellationToken);
        return new StartTripResult(report.Id.Value, AlreadyReported: false, report.CorrelationId);
    }
}

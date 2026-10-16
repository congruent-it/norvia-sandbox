using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Reports.EndTrip;

/// <summary>
/// Records the Driver's End Trip and queues <c>TripEndReported</c> in the outbox. Both are saved in one transaction when the report is added, so the event is
/// published exactly once and only for a report that was recorded. Whether the Trip can be ended is the Domain's to say (<see cref="TripEndRules"/>); the TMS
/// decides all the same, and its refusal comes back as the report's outcome.
/// </summary>
public sealed class EndTripHandler(
    IReadRepository<DriverTrip> trips,
    IRepository<DriverReport> reports,
    IDriverEventOutbox outbox,
    TimeProvider time) : ICommandHandler<EndTripCommand, Result<EndTripResult>>
{
    public async ValueTask<Result<EndTripResult>> Handle(EndTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tripNumber = new TripNumber(command.TripNumber);
        var driverId = new DriverId(command.DriverId);

        // A Driver sees only their own Trips, so another Driver's is as unknown as one that doesn't exist.
        if (await trips.GetByIdAsync(tripNumber, cancellationToken) is not { } trip || trip.DriverId != driverId)
        {
            return Result.NotFound();
        }

        var held = await reports.ListAsync(new ReportsOfTripsSpecification([tripNumber]), cancellationToken);
        switch (TripEndRules.Decide(trip.Details, held))
        {
            case TripEndDecision.Repeat repeat:
                return new EndTripResult(repeat.ReportId.Value, AlreadyReported: true);
            case TripEndDecision.NotStarted:
                return Result.Conflict(EndTripTexts.NotStarted);
            case TripEndDecision.StopsOpen:
                return Result.Conflict(EndTripTexts.StopsOpen);
        }

        var now = time.GetUtcNow();
        var report = DriverReport.EndTrip(new ReportId(Guid.CreateVersion7(now)), tripNumber, driverId, trip.Version, now, Guid.CreateVersion7(now).ToString());

        // Queued before the report is saved: the save writes the report and the outbox message together.
        outbox.Add(new TripEndReportedV1(report.Id.Value, report.EventTime, report.CorrelationId, tripNumber.Value, report.VersionHeld, driverId.Value));
        await reports.AddAsync(report, cancellationToken);
        return new EndTripResult(report.Id.Value, AlreadyReported: false, report.CorrelationId);
    }
}

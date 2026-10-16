using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Reports.ReportStop;

/// <summary>
/// Records the Driver's report on a Stop and queues <c>StopCompletedReported</c> in the outbox. Both are saved in one transaction when the report is added, so the
/// event is published exactly once and only for a report that was recorded. What the Driver can report is the Domain's to say (<see cref="StopReportRules"/>); the
/// TMS decides all the same, and its refusal comes back as the report's outcome.
/// </summary>
public sealed class ReportStopHandler(
    IReadRepository<DriverTrip> trips,
    IRepository<DriverReport> reports,
    IDriverEventOutbox outbox,
    TimeProvider time) : ICommandHandler<ReportStopCommand, Result<ReportStopResult>>
{
    public async ValueTask<Result<ReportStopResult>> Handle(ReportStopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tripNumber = new TripNumber(command.TripNumber);
        var driverId = new DriverId(command.DriverId);

        // A Driver sees only their own Trips, so another Driver's is as unknown as one that doesn't exist.
        if (await trips.GetByIdAsync(tripNumber, cancellationToken) is not { } trip || trip.DriverId != driverId)
        {
            return Result.NotFound();
        }

        var stopRef = new StopRef(command.ShipmentNumber, command.Stop == StopKindDto.Collection ? StopKind.Collection : StopKind.Delivery);
        var held = await reports.ListAsync(new ReportsOfTripsSpecification([tripNumber]), cancellationToken);
        if (TripProgress.Of(trip.Details, held).FirstOrDefault(status => status.Stop == stopRef && !status.TakenOff) is not { Details: { } details } status)
        {
            return Result.NotFound();
        }

        var kind = command.Action.ToDomain();
        var remark = command.Remark?.Trim();
        switch (StopReportRules.Decide(status, kind, remark))
        {
            case StopReportDecision.Invalid invalid:
                return Result.Invalid([.. invalid.Problems.Select(problem => new ValidationError(problem.Field, problem.Message))]);
            case StopReportDecision.Repeat repeat:
                return new ReportStopResult(repeat.ReportId.Value, AlreadyReported: true);
            case StopReportDecision.AlreadyCompleted:
                return Result.Conflict("The Stop has an outcome already.");
            case StopReportDecision.Unavailable unavailable:
                return Result.Conflict(StopBlocks.Text(unavailable.Block) ?? "The Stop cannot be completed now.");
        }

        var now = time.GetUtcNow();
        var report = DriverReport.ReportStop(
            new ReportId(Guid.CreateVersion7(now)), kind, stopRef, new ReportOrigin(tripNumber, driverId, trip.Version, now, Guid.CreateVersion7(now).ToString()), remark);

        // Queued before the report is saved: the save writes the report and the outbox message together.
        outbox.Add(new StopCompletedReportedV1(
            report.Id.Value, report.EventTime, report.CorrelationId, tripNumber.Value, report.VersionHeld, driverId.Value, command.ShipmentNumber,
            command.Stop.ToString(), kind.ToTrackingEventType(), new StopLocationV1(details.Address!.City!, details.Address.Postcode!, details.Address.Country!), report.Remark));
        await reports.AddAsync(report, cancellationToken);
        return new ReportStopResult(report.Id.Value, AlreadyReported: false, report.CorrelationId);
    }
}

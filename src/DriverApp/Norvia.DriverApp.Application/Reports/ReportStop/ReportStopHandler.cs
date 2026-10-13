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
/// event is published exactly once and only for a report that was recorded. The Driver app offers only what the Driver can do now (a Stop is completed once, in
/// any order, but a delivery waits for its collection); the TMS decides all the same, and its refusal comes back as the report's outcome.
/// </summary>
public sealed class ReportStopHandler(
    IReadRepository<DriverTrip> trips,
    IRepository<DriverReport> reports,
    IDriverEventOutbox outbox,
    TimeProvider time) : ICommandHandler<ReportStopCommand, Result<ReportStopResult>>
{
    /// <summary>The longest remark the TMS keeps of a Tracking event.</summary>
    internal const int MaximumRemarkLength = 500;

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

        var held = await reports.ListAsync(new ReportsOfTripsSpecification([tripNumber]), cancellationToken);
        var stop = StopProgress.Of(trip.Details, held).FirstOrDefault(view => view.ShipmentNumber == command.ShipmentNumber && view.Kind == command.Stop && !view.TakenOff);
        if (stop?.Details is not { } details)
        {
            return Result.NotFound();
        }

        var kind = KindOf(command.Action);
        var remark = command.Remark?.Trim();
        if (Validate(command, details, remark) is { Count: > 0 } errors)
        {
            return Result.Invalid(errors);
        }

        if (kind == ReportKind.Damaged)
        {
            if (!stop.CanReportDamage)
            {
                return Result.Conflict(StopProgress.TripNotStarted);
            }

            // Pressing Report damage twice with the same words while the first is on its way is one report.
            if (stop.Reports.FirstOrDefault(report => report.Kind == ReportKindDto.Damaged && report.State == ReportStateDto.Sending && report.Remark == remark) is { } sending)
            {
                return new ReportStopResult(sending.Id, AlreadyReported: true);
            }
        }
        else if (stop.Completed)
        {
            return stop.Outcome!.Kind == ReportKindDtoOf(kind)
                ? new ReportStopResult(stop.Outcome.Id, AlreadyReported: true)
                : Result.Conflict("The Stop has an outcome already.");
        }
        else if (!stop.CanComplete)
        {
            return Result.Conflict(stop.Blocked ?? "The Stop cannot be completed now.");
        }

        var now = time.GetUtcNow();
        var report = DriverReport.ReportStop(
            new ReportId(Guid.CreateVersion7(now)), kind, tripNumber, command.ShipmentNumber, StopKindOf(command.Stop), driverId, trip.Version, now, Guid.CreateVersion7(now).ToString(), remark);

        // Queued before the report is saved: the save writes the report and the outbox message together.
        outbox.Add(new StopCompletedReportedV1(
            report.Id.Value, report.EventTime, report.CorrelationId, tripNumber.Value, report.VersionHeld, driverId.Value, command.ShipmentNumber,
            command.Stop.ToString(), TrackingEventTypeOf(kind), new StopLocationV1(details.Address!.City!, details.Address.Postcode!, details.Address.Country!), report.Remark));
        await reports.AddAsync(report, cancellationToken);
        return new ReportStopResult(report.Id.Value, AlreadyReported: false);
    }

    private static List<ValidationError> Validate(ReportStopCommand command, StopDto stop, string? remark)
    {
        var errors = new List<ValidationError>();
        var fits = command.Action switch
        {
            StopActionDto.Collected or StopActionDto.CollectionFailed => command.Stop == StopKindDto.Collection,
            StopActionDto.DeliveryFailed => command.Stop == StopKindDto.Delivery,
            _ => true,
        };
        if (!fits)
        {
            errors.Add(new("action", $"{command.Action} is not reported on a {command.Stop.ToString().ToLowerInvariant()} Stop."));
        }

        if (command.Action != StopActionDto.Collected && string.IsNullOrWhiteSpace(remark))
        {
            errors.Add(new("remark", "A remark is required."));
        }

        if (remark?.Length > MaximumRemarkLength)
        {
            errors.Add(new("remark", $"A remark is at most {MaximumRemarkLength} characters."));
        }

        // The TMS records where a Stop report happened, and the Stop's address is that place.
        if (stop.Address is not { City: { Length: > 0 }, Postcode: { Length: > 0 }, Country: { Length: > 0 } })
        {
            errors.Add(new("address", "The Stop has no complete address to report from."));
        }

        return errors;
    }

    private static ReportKind KindOf(StopActionDto action) => action switch
    {
        StopActionDto.Collected => ReportKind.Collected,
        StopActionDto.CollectionFailed => ReportKind.CollectionFailed,
        StopActionDto.DeliveryFailed => ReportKind.DeliveryFailed,
        _ => ReportKind.Damaged,
    };

    private static ReportKindDto ReportKindDtoOf(ReportKind kind) => kind switch
    {
        ReportKind.Collected => ReportKindDto.Collected,
        ReportKind.CollectionFailed => ReportKindDto.CollectionFailed,
        ReportKind.DeliveryFailed => ReportKindDto.DeliveryFailed,
        _ => ReportKindDto.Damaged,
    };

    private static StopKind StopKindOf(StopKindDto kind) => kind == StopKindDto.Collection ? StopKind.Collection : StopKind.Delivery;

    private static string TrackingEventTypeOf(ReportKind kind) => kind.ToString();
}

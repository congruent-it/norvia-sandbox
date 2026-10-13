using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Reports.Deliver;

/// <summary>
/// Records the Driver's Deliver: the Delivered that completes the Stop and the Proof of delivery, as two reports that share one correlation ID. Their files go to
/// the transit container first, then the reports and the outbox's <c>StopCompletedReported</c> and <c>ProofOfDeliveryCaptured</c> are saved in one transaction,
/// so both events are published exactly once and only for a Deliver that was recorded. A file stored for a Deliver that wasn't saved is left to the container's
/// lifecycle rule. What the Driver can do and what the form asks are the Domain's to say; the TMS decides all the same.
/// </summary>
public sealed class DeliverHandler(
    IReadRepository<DriverTrip> trips,
    IRepository<DriverReport> reports,
    IProofOfDeliveryTransit transit,
    IDriverEventOutbox outbox,
    TimeProvider time) : ICommandHandler<DeliverCommand, Result<DeliverResult>>
{
    public async ValueTask<Result<DeliverResult>> Handle(DeliverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tripNumber = new TripNumber(command.TripNumber);
        var driverId = new DriverId(command.DriverId);

        // A Driver sees only their own Trips, so another Driver's is as unknown as one that doesn't exist.
        if (await trips.GetByIdAsync(tripNumber, cancellationToken) is not { } trip || trip.DriverId != driverId)
        {
            return Result.NotFound();
        }

        var stopRef = new StopRef(command.ShipmentNumber, StopKind.Delivery);
        var held = await reports.ListAsync(new ReportsOfTripsSpecification([tripNumber]), cancellationToken);
        if (TripProgress.Of(trip.Details, held).FirstOrDefault(status => status.Stop == stopRef && !status.TakenOff) is not { Details: { } details } status)
        {
            return Result.NotFound();
        }

        var condition = command.Condition is { } given ? (DeliveryCondition?)(given == DeliveryConditionDto.Reservations ? DeliveryCondition.Reservations : DeliveryCondition.Clean) : null;
        var reservations = ProofOfDeliveryRules.ReservationsRemark(condition, command.Remark);
        switch (StopReportRules.Decide(status, ReportKind.Delivered, reservations))
        {
            case StopReportDecision.Invalid invalid:
                return Result.Invalid([.. invalid.Problems.Select(problem => new ValidationError(problem.Field, problem.Message))]);
            case StopReportDecision.Repeat repeat:
                return new DeliverResult(repeat.ReportId.Value, AlreadyReported: true);
            case StopReportDecision.AlreadyCompleted:
                return Result.Conflict("The Stop has an outcome already.");
            case StopReportDecision.Unavailable unavailable:
                return Result.Conflict(StopBlocks.Text(unavailable.Block) ?? "The Stop cannot be delivered now.");
        }

        var form = new ProofOfDeliveryForm(
            command.ReceivedBy?.Trim(),
            condition,
            command.Remark?.Trim(),
            command.Signature is { } signature ? new ProofFileInfo(signature.ContentType, signature.Content.Length) : null,
            [.. command.Photos.Select(photo => new ProofFileInfo(photo.ContentType, photo.Content.Length))]);
        if (ProofOfDeliveryRules.Check(form) is { Count: > 0 } problems)
        {
            return Result.Invalid([.. problems.Select(problem => new ValidationError(problem.Field, problem.Message))]);
        }

        var now = time.GetUtcNow();
        var origin = new ReportOrigin(tripNumber, driverId, trip.Version, now, Guid.CreateVersion7(now).ToString());
        var delivered = DriverReport.Deliver(new ReportId(Guid.CreateVersion7(now)), new ReportId(Guid.CreateVersion7(now)), stopRef, origin, reservations);

        // The Driver app's own ID for the Proof of delivery is the ID of its report, which is unique and names the files in the container.
        var documentId = delivered.ProofOfDelivery.Id.Value.ToString();
        // The form was checked above, so the signature, the name and the condition are there.
        var files = await StoreFilesAsync(documentId, command, cancellationToken);
        var location = new StopLocationV1(details.Address!.City!, details.Address.Postcode!, details.Address.Country!);

        // Queued before the reports are saved: the save writes the reports and the outbox messages together. The Tracking event goes first, as the TMS
        // records the Proof of delivery of a Shipment it has delivered.
        outbox.Add(new StopCompletedReportedV1(
            delivered.Delivered.Id.Value, now, origin.CorrelationId, tripNumber.Value, trip.Version, driverId.Value, command.ShipmentNumber, "Delivery", ReportKind.Delivered.ToTrackingEventType(), location, null));
        outbox.Add(new ProofOfDeliveryCapturedV1(
            delivered.ProofOfDelivery.Id.Value, now, origin.CorrelationId, tripNumber.Value, trip.Version, driverId.Value, command.ShipmentNumber, documentId, form.ReceivedBy!, now, location,
            condition!.Value.ToString(), reservations, files));
        await reports.AddRangeAsync([delivered.Delivered, delivered.ProofOfDelivery], cancellationToken);
        return new DeliverResult(delivered.Delivered.Id.Value, AlreadyReported: false);
    }

    private async Task<IReadOnlyList<ProofOfDeliveryFileV1>> StoreFilesAsync(string documentId, DeliverCommand command, CancellationToken cancellationToken)
    {
        var files = new List<ProofOfDeliveryFileV1>();
        var given = new List<(string Name, ProofFile File)> { (ProofOfDeliveryRules.SignatureName, command.Signature!) };
        given.AddRange(command.Photos.Select((photo, index) => (ProofOfDeliveryRules.PhotoName(index + 1, photo.ContentType), photo)));

        foreach (var (name, file) in given)
        {
            var reference = await transit.StoreAsync($"{documentId}/{name}", file.ContentType, file.Content, cancellationToken);
            files.Add(new ProofOfDeliveryFileV1(name, file.ContentType, reference));
        }

        return files;
    }
}

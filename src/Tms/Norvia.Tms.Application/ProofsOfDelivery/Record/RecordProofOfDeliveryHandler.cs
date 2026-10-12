using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

public sealed class RecordProofOfDeliveryHandler(
    IRepository<ProofOfDelivery> proofsOfDelivery,
    IRepository<CarrierBooking> carrierBookings,
    IRepository<Shipment> shipments,
    IReadRepository<Trip> trips,
    IReadRepository<TrackingEvent> trackingEvents,
    IProofOfDeliveryFiles files,
    ExceptionCauses exceptionCauses,
    BillingCheck billing,
    ProofOfDeliveryRules rules,
    TimeProvider time)
    : ICommandHandler<RecordProofOfDeliveryCommand, Result<RecordProofOfDeliveryResult>>
{
    public async ValueTask<Result<RecordProofOfDeliveryResult>> Handle(RecordProofOfDeliveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var report = command.Report;
        if (command.Files.Count != report.Files.Count)
        {
            throw new ArgumentException("Every file of the report needs its content.", nameof(command));
        }

        // A retry of a document ID the source already sent is harmless: it gets the original result and nothing is published. This is
        // checked first, whatever has happened on the Trip or the booking since.
        var original = await proofsOfDelivery.SingleOrDefaultAsync(new ProofOfDeliveryByDocumentIdSpec(report.Source, report.DocumentId), cancellationToken);
        if (original?.IsRepeatOf(report) is true)
        {
            var originalShipment = await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(original.ShipmentNumber), cancellationToken)
                ?? throw new InvalidOperationException($"Proof of delivery {original.Id} names Shipment {original.ShipmentNumber}, which the TMS doesn't have.");
            return RecordProofOfDeliveryResult.RepeatOf(originalShipment, original);
        }

        var now = time.GetUtcNow();
        var found = report.OwnFleet is { } ownFleet
            ? await FindByTripAsync(report, ownFleet, now, cancellationToken)
            : await FindByBookingAsync(report, now, cancellationToken);
        if (found.Refusal is { } refusal)
        {
            return ProofOfDeliveryRefused.Because<RecordProofOfDeliveryResult>(refusal);
        }

        // Without a refusal, a Shipment was found.
        var shipment = found.Shipment ?? throw new InvalidOperationException($"Proof of delivery {report.DocumentId} found no Shipment and was not refused.");

        if (ProofOfDeliveryRules.CheckFiles(report.Files, command.Files) is { } filesRefusal)
        {
            return ProofOfDeliveryRefused.Because<RecordProofOfDeliveryResult>(filesRefusal);
        }

        report = report with { Files = ProofOfDeliveryRules.Judged(report.Files, command.Files) };

        // The files go to storage first, so a Proof of delivery is never recorded without them. One that fails to save
        // leaves its files behind, unreferenced, which does no harm.
        for (var i = 0; i < report.Files.Count; i++)
        {
            await files.StoreAsync(shipment.Number, report.Files[i], command.Files[i], cancellationToken);
        }

        // The record, the Exception it opens or joins, the Transport Order becoming Billable and the events they raise, which go to the
        // outbox, are saved in one transaction.
        // A new document ID for a Shipment that already has a current Proof of delivery replaces it: the earlier one is kept, superseded.
        var replaced = await proofsOfDelivery.SingleOrDefaultAsync(new TrackedCurrentProofOfDeliverySpec(shipment.Id), cancellationToken);
        var correlationId = new CorrelationId(command.CorrelationId);
        var proofOfDelivery = ProofOfDelivery.Record(shipment, report, correlationId, now, replaced);
        var cause = ExceptionCause.From(proofOfDelivery, now);
        if (cause is not null)
        {
            await exceptionCauses.OpenOrAddAsync(shipment, cause, cancellationToken);
        }
        else if (ExceptionTypes.ClearedBy(proofOfDelivery.Condition) is { } cleared)
        {
            await exceptionCauses.ClearAsync(shipment, cleared, cancellationToken);
        }

        // A Proof of delivery can complete the conditions of the Transport Order of a Delivered Shipment to be Billable (P9).
        if (shipment.Status == ShipmentStatus.Delivered)
        {
            await billing.CheckAsync(shipment, new BillingChange(correlationId, now, ProofOfDeliveryRecorded: true, ExceptionOpened: cause is not null), cancellationToken);
        }

        await proofsOfDelivery.AddAsync(proofOfDelivery, cancellationToken);
        await proofsOfDelivery.SaveChangesAsync(cancellationToken);
        return RecordProofOfDeliveryResult.From(shipment, proofOfDelivery);
    }

    // A Carrier's Proof of delivery finds its Shipment by the Carrier booking reference, or else the Carrier booking number.
    private async Task<(Shipment? Shipment, ProofOfDeliveryRefusal? Refusal)> FindByBookingAsync(ProofOfDeliveryReport report, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (report.Source.CarrierId is not { } carrierId || report.CarrierBookingReference is not { } reference)
        {
            return (null, ProofOfDeliveryRules.ShipmentNotFound(report.CarrierBookingReference ?? string.Empty));
        }

        var booking = await carrierBookings.SingleOrDefaultAsync(new ConfirmedCarrierBookingByReferenceSpec(carrierId, reference), cancellationToken);
        if (booking is null && report.CarrierBookingNumber is { } number)
        {
            booking = await carrierBookings.SingleOrDefaultAsync(new ConfirmedCarrierBookingByNumberSpec(carrierId, number), cancellationToken);
        }

        if (booking is null)
        {
            return (null, ProofOfDeliveryRules.ShipmentNotFound(reference));
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(booking.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        return (shipment, ProofOfDeliveryRules.CheckShipment(shipment) ?? rules.CheckDeliveryTime(report.DeliveryTime, now));
    }

    // A Driver's Proof of delivery (P12) finds its Shipment by Trip, and the Trip's rules decide it.
    private async Task<(Shipment? Shipment, ProofOfDeliveryRefusal? Refusal)> FindByTripAsync(
        ProofOfDeliveryReport report, OwnFleetReport own, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trip = await trips.FirstOrDefaultAsync(new TripByNumberSpec(own.TripNumber), cancellationToken);
        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(own.ShipmentNumber), cancellationToken);
        var outcomes = StopOutcomes.From(trip is null ? [] : await trackingEvents.ListAsync(new TrackingEventsOfTripSpec([own.TripNumber]), cancellationToken));

        return (shipment, rules.CheckOwnFleetReport(trip, shipment, report, outcomes, now));
    }
}

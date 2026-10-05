using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

public sealed class RecordProofOfDeliveryHandler(
    IRepository<ProofOfDelivery> proofsOfDelivery,
    IRepository<CarrierBooking> carrierBookings,
    IRepository<Shipment> shipments,
    IProofOfDeliveryFiles files,
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

        // Own fleet reports through P12, which finds its Shipment by Trip, not by a Carrier booking reference.
        if (report.Source.CarrierId is not { } carrierId)
        {
            return ProofOfDeliveryRefused.Because<RecordProofOfDeliveryResult>(ProofOfDeliveryRules.ShipmentNotFound(report.CarrierBookingReference));
        }

        // A Carrier's retry of a document ID it already sent is harmless: it gets the original result and nothing is published.
        var original = await proofsOfDelivery.SingleOrDefaultAsync(new ProofOfDeliveryByDocumentIdSpec(report.Source, report.DocumentId), cancellationToken);
        if (original is not null)
        {
            var originalShipment = await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(original.ShipmentNumber), cancellationToken)
                ?? throw new InvalidOperationException($"Proof of delivery {original.Id} names Shipment {original.ShipmentNumber}, which the TMS doesn't have.");
            return RecordProofOfDeliveryResult.RepeatOf(originalShipment, original);
        }

        var booking = await carrierBookings.SingleOrDefaultAsync(
            new ConfirmedCarrierBookingByReferenceSpec(carrierId, report.CarrierBookingReference), cancellationToken);
        if (booking is null && report.CarrierBookingNumber is { } number)
        {
            booking = await carrierBookings.SingleOrDefaultAsync(new ConfirmedCarrierBookingByNumberSpec(carrierId, number), cancellationToken);
        }

        if (booking is null)
        {
            return ProofOfDeliveryRefused.Because<RecordProofOfDeliveryResult>(ProofOfDeliveryRules.ShipmentNotFound(report.CarrierBookingReference));
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(booking.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        var refusal = ProofOfDeliveryRules.CheckShipment(shipment)
            ?? rules.CheckDeliveryTime(report.DeliveryTime, time.GetUtcNow())
            ?? ProofOfDeliveryRules.CheckFiles(report.Files, command.Files);
        if (refusal is not null)
        {
            return ProofOfDeliveryRefused.Because<RecordProofOfDeliveryResult>(refusal);
        }

        report = report with { Files = ProofOfDeliveryRules.Judged(report.Files, command.Files) };

        // The files go to storage first, so a Proof of delivery is never recorded without them. One that fails to save
        // leaves its files behind, unreferenced, which does no harm.
        for (var i = 0; i < report.Files.Count; i++)
        {
            await files.StoreAsync(shipment.Number, report.Files[i], command.Files[i], cancellationToken);
        }

        // The record and the event it raises, which goes to the outbox, are saved in one transaction.
        // A new document ID for a Shipment that already has a current Proof of delivery replaces it: the earlier one is kept, superseded.
        var replaced = await proofsOfDelivery.SingleOrDefaultAsync(new CurrentProofOfDeliveryToReplaceSpec(shipment.Id), cancellationToken);
        var proofOfDelivery = ProofOfDelivery.Record(shipment, report, new CorrelationId(command.CorrelationId), time.GetUtcNow(), replaced);
        await proofsOfDelivery.AddAsync(proofOfDelivery, cancellationToken);
        await proofsOfDelivery.SaveChangesAsync(cancellationToken);
        return RecordProofOfDeliveryResult.From(shipment, proofOfDelivery);
    }
}

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

        var booking = await carrierBookings.SingleOrDefaultAsync(
            new ConfirmedCarrierBookingByReferenceSpec(carrierId, report.CarrierBookingReference), cancellationToken);
        if (booking is null)
        {
            return ProofOfDeliveryRefused.Because<RecordProofOfDeliveryResult>(ProofOfDeliveryRules.ShipmentNotFound(report.CarrierBookingReference));
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(booking.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        if (ProofOfDeliveryRules.CheckShipment(shipment) is { } refusal)
        {
            return ProofOfDeliveryRefused.Because<RecordProofOfDeliveryResult>(refusal);
        }

        // The files go to storage first, so a Proof of delivery is never recorded without them. One that fails to save
        // leaves its files behind, unreferenced, which does no harm.
        for (var i = 0; i < report.Files.Count; i++)
        {
            await files.StoreAsync(shipment.Number, report.Files[i], command.Files[i], cancellationToken);
        }

        // The record and the event it raises, which goes to the outbox, are saved in one transaction.
        var proofOfDelivery = ProofOfDelivery.Record(shipment, report, new CorrelationId(command.CorrelationId), time.GetUtcNow());
        await proofsOfDelivery.AddAsync(proofOfDelivery, cancellationToken);
        await proofsOfDelivery.SaveChangesAsync(cancellationToken);
        return RecordProofOfDeliveryResult.From(shipment, proofOfDelivery);
    }
}

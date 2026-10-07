using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

public sealed class ConfirmCarrierBookingHandler(IRepository<CarrierBooking> carrierBookings, IRepository<Shipment> shipments, TimeProvider time)
    : ICommandHandler<ConfirmCarrierBookingCommand, Result<CarrierBookingAnswerResult>>
{
    public async ValueTask<Result<CarrierBookingAnswerResult>> Handle(ConfirmCarrierBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (booking, refusal) = await CarrierBookingAnswerLookup.ToAnswerAsync(carrierBookings, command.CarrierBookingNumber, command.CarrierId, cancellationToken);
        if (refusal is not null)
        {
            return CarrierBookingRefused.Because<CarrierBookingAnswerResult>(refusal);
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(booking!.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        if (booking.Status != CarrierBookingStatus.Requested)
        {
            // The booking already has an answer: the same one succeeds again as a repeat, a different one is refused.
            return booking.IsRepeatOfConfirm(command.CarrierBookingReference, command.PlannedCollectionTime)
                ? CarrierBookingAnswerResult.From(booking, shipment, repeat: true)
                : CarrierBookingRefused.Because<CarrierBookingAnswerResult>(CarrierBookingRules.NotOpen(booking));
        }

        // Tracking events and Proofs of delivery find their Shipment by the Carrier's reference, so one Carrier may not hold it twice.
        if (await carrierBookings.AnyAsync(new ConfirmedCarrierBookingByReferenceSpec(booking.CarrierId, command.CarrierBookingReference), cancellationToken))
        {
            return CarrierBookingRefused.Because<CarrierBookingAnswerResult>(CarrierBookingRules.ReferenceInUse(booking, command.CarrierBookingReference));
        }

        if (!booking.Confirm(
                command.CarrierBookingReference, command.PlannedCollectionTime, shipment.TransportOrderNumber, new CorrelationId(command.CorrelationId), time.GetUtcNow())
            || !shipment.Book())
        {
            throw new InvalidOperationException($"Carrier booking {booking.Number} passed the answer rules but wasn't confirmed.");
        }

        // Confirming the booking saves the tracked Shipment's move to Booked with it, in one transaction (P4).
        await carrierBookings.SaveChangesAsync(cancellationToken);
        return CarrierBookingAnswerResult.From(booking, shipment, repeat: false);
    }
}

using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
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

        var booking = await CarrierBookingLookup.ToChangeAsync(carrierBookings, command.CarrierBookingNumber, cancellationToken);
        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(booking.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        // This slice only ever answers a Requested booking (the happy path); BookingNotOpen's refusal is a later ticket.
        if (!booking.Confirm(
                command.CarrierBookingReference, command.PlannedCollectionTime, shipment.TransportOrderNumber, new CorrelationId(command.CorrelationId), time.GetUtcNow())
            || !shipment.Book())
        {
            throw new InvalidOperationException($"Carrier booking {booking.Number} is {booking.Status}, not Requested.");
        }

        // Confirming the booking saves the tracked Shipment's move to Booked with it, in one transaction (P4).
        await carrierBookings.SaveChangesAsync(cancellationToken);
        return new CarrierBookingAnswerResult(booking.Number.Value, booking.Status, shipment.Number.Value, shipment.Status, command.CorrelationId);
    }
}

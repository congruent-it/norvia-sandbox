using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

public sealed class RejectCarrierBookingHandler(IRepository<CarrierBooking> carrierBookings, IRepository<Shipment> shipments, TimeProvider time)
    : ICommandHandler<RejectCarrierBookingCommand, Result<CarrierBookingAnswerResult>>
{
    public async ValueTask<Result<CarrierBookingAnswerResult>> Handle(RejectCarrierBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (booking, refusal) = await CarrierBookingAnswerLookup.ToAnswerAsync(carrierBookings, command.CarrierBookingNumber, command.CarrierId, cancellationToken);
        if (refusal is not null)
        {
            return CarrierBookingAnswerRefused.Because<CarrierBookingAnswerResult>(refusal);
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(booking!.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        if (booking.Status != CarrierBookingStatus.Requested)
        {
            // The booking already has an answer: the same one succeeds again as a repeat, a different one is refused.
            return booking.IsRepeatOfReject(command.ReasonCode, command.ReasonText)
                ? CarrierBookingAnswerResult.From(booking, shipment, repeat: true)
                : CarrierBookingAnswerRefused.Because<CarrierBookingAnswerResult>(CarrierBookingAnswerRules.NotOpen(booking));
        }

        if (!booking.Reject(command.ReasonCode, command.ReasonText, new CorrelationId(command.CorrelationId), time.GetUtcNow()) || !shipment.ReturnToPlanned())
        {
            throw new InvalidOperationException($"Carrier booking {booking.Number} passed the answer rules but wasn't rejected.");
        }

        // Rejecting the booking saves the tracked Shipment's move back to Planned with it, in one transaction (P4). Its
        // move publishes no event of its own: CarrierBookingRejected already says it.
        await carrierBookings.SaveChangesAsync(cancellationToken);
        return CarrierBookingAnswerResult.From(booking, shipment, repeat: false);
    }
}


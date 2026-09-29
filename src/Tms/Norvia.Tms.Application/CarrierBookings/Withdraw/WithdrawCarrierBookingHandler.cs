using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.CarrierBookings.Answer;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.CarrierBookings.Withdraw;

public sealed class WithdrawCarrierBookingHandler(IRepository<CarrierBooking> carrierBookings, IRepository<Shipment> shipments, TimeProvider time)
    : ICommandHandler<WithdrawCarrierBookingCommand, Result<CarrierBookingWithdrawResult>>
{
    public async ValueTask<Result<CarrierBookingWithdrawResult>> Handle(WithdrawCarrierBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (booking, refusal) = await CarrierBookingLookup.ToBookingAsync(carrierBookings, command.CarrierBookingNumber, cancellationToken);
        if (refusal is not null)
        {
            return CarrierBookingRefused.Because<CarrierBookingWithdrawResult>(refusal);
        }

        // ShipmentToChangeSpec has no status filter, so this fetch is safe ahead of the repeat and status checks below;
        // if that ever changes, move it after them so a withdrawal of an answered booking still gets BookingNotOpen.
        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(booking!.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");

        var key = new IdempotencyKey(command.WithdrawalKey);
        if (booking.WithdrawalUnder(key) is { } earlier)
        {
            return ResultOf(booking, shipment, earlier.Value, repeat: true);
        }

        if (booking.Status != CarrierBookingStatus.Requested)
        {
            return CarrierBookingRefused.Because<CarrierBookingWithdrawResult>(CarrierBookingAnswerRules.NotOpen(booking));
        }

        if (!booking.Withdraw(key, new CorrelationId(command.CorrelationId), time.GetUtcNow()) || !shipment.ReturnToPlanned())
        {
            throw new InvalidOperationException($"Carrier booking {booking.Number} passed the withdrawal rules but wasn't withdrawn.");
        }

        // Withdrawing the booking saves the tracked Shipment's move back to Planned with it, in one transaction (P4).
        await carrierBookings.SaveChangesAsync(cancellationToken);
        return ResultOf(booking, shipment, command.CorrelationId, repeat: false);
    }

    private static CarrierBookingWithdrawResult ResultOf(CarrierBooking booking, Shipment shipment, string correlationId, bool repeat) =>
        new(booking.Number.Value, booking.Status, shipment.Number.Value, shipment.Status, correlationId, repeat);
}

using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Application.Exceptions.CancelCarrierBooking;

public sealed class CancelCarrierBookingHandler(
    IRepository<ExceptionCase> exceptions,
    IRepository<Shipment> shipments,
    IRepository<CarrierBooking> carrierBookings,
    TimeProvider time)
    : ICommandHandler<CancelCarrierBookingCommand, Result<CancelCarrierBookingResult>>
{
    public async ValueTask<Result<CancelCarrierBookingResult>> Handle(CancelCarrierBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ExceptionNumber.TryParse(command.ExceptionNumber, out var number)
            || await exceptions.SingleOrDefaultAsync(new ExceptionToResolveSpec(number), cancellationToken) is not { } exception)
        {
            return ExceptionRefused.Because<CancelCarrierBookingResult>(ExceptionRefusal.NotFound(command.ExceptionNumber));
        }

        // An Exception always has its Shipment, so a missing one is corrupt data, not something the Planner can fix.
        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(exception.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Exception {exception.Number} has no Shipment {exception.ShipmentNumber}.");
        var bookings = await carrierBookings.ListAsync(new CarrierBookingsOfShipmentToChangeSpec(shipment.Id), cancellationToken);

        var key = new IdempotencyKey(command.CancellationKey);
        if (bookings.FirstOrDefault(booking => booking.CancellationUnder(key) is not null) is { } earlier)
        {
            return ResultOf(exception, earlier, earlier.CancellationCorrelationId?.Value ?? throw new InvalidOperationException($"Carrier booking {earlier.Number} was cancelled without a correlation ID."), repeat: true);
        }

        if (exception.Status != ExceptionStatus.Open)
        {
            return ExceptionRefused.Because<CancelCarrierBookingResult>(
                new(ExceptionRefusalCode.ExceptionNotOpen, Invariant($"Exception {exception.Number} is {exception.Status}, not Open.")));
        }

        // Own fleet and a Shipment without a Confirmed booking have nothing to cancel, whatever their status.
        var booking = bookings.FirstOrDefault(booking => booking.Status == CarrierBookingStatus.Confirmed);
        if (booking is null)
        {
            return ExceptionRefused.Because<CancelCarrierBookingResult>(
                new(ExceptionRefusalCode.NoConfirmedCarrierBooking, Invariant($"Shipment {shipment.Number} has no Confirmed Carrier booking to cancel.")));
        }

        // After collection the Carrier holds the goods, so nothing is cancelled (ADR 0010).
        if (shipment.Status != ShipmentStatus.Booked)
        {
            return ExceptionRefused.Because<CancelCarrierBookingResult>(
                new(ExceptionRefusalCode.ShipmentAlreadyCollected, Invariant($"Shipment {shipment.Number} is {shipment.Status}, so its Carrier booking can't be cancelled after collection.")));
        }

        if (!booking.Cancel(command.ReasonText, exception.Number, key, new CorrelationId(command.CorrelationId), time.GetUtcNow())
            || !shipment.ReturnToPlannedAfterCancellation())
        {
            throw new InvalidOperationException($"Carrier booking {booking.Number} passed the cancellation rules but wasn't cancelled.");
        }

        // Cancelling the booking saves the tracked Shipment's move back to Planned with it, in one transaction (P7).
        await carrierBookings.SaveChangesAsync(cancellationToken);
        return ResultOf(exception, booking, command.CorrelationId, repeat: false);
    }

    private static CancelCarrierBookingResult ResultOf(ExceptionCase exception, CarrierBooking booking, string correlationId, bool repeat) =>
        new(exception.Number.Value, booking.ShipmentNumber.Value, booking.Number.Value, correlationId, repeat);
}

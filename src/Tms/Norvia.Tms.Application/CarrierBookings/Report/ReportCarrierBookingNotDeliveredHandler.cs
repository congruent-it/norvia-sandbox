using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.CarrierBookings.Report;

public sealed class ReportCarrierBookingNotDeliveredHandler(IRepository<CarrierBooking> carrierBookings, IRepository<Shipment> shipments, TimeProvider time)
    : ICommandHandler<ReportCarrierBookingNotDeliveredCommand, Result<CarrierBookingReportResult>>
{
    public async ValueTask<Result<CarrierBookingReportResult>> Handle(ReportCarrierBookingNotDeliveredCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (booking, refusal) = await CarrierBookingLookup.ToBookingAsync(carrierBookings, command.CarrierBookingNumber, cancellationToken);
        if (refusal is not null)
        {
            return CarrierBookingRefused.Because<CarrierBookingReportResult>(refusal);
        }

        if (!booking!.ReportNotDelivered(command.StatusCode, command.Message, new CorrelationId(command.CorrelationId), time.GetUtcNow()))
        {
            // No longer Requested, for example withdrawn while the report was in flight: ignored, changing nothing.
            return new CarrierBookingReportResult(booking.Number.Value, booking.Status, Ignored: true);
        }

        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(booking.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Carrier booking {booking.Number} names Shipment {booking.ShipmentNumber}, which the TMS doesn't have.");
        if (!shipment.ReturnToPlanned())
        {
            throw new InvalidOperationException($"Carrier booking {booking.Number} became NotDelivered but Shipment {shipment.Number} wasn't Released.");
        }

        // The report's move saves the tracked Shipment's move back to Planned with it, in one transaction (P4). Its
        // move publishes no event of its own: CarrierBookingNotDelivered already says it.
        await carrierBookings.SaveChangesAsync(cancellationToken);
        return new CarrierBookingReportResult(booking.Number.Value, booking.Status);
    }
}

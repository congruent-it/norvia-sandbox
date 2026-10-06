using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.CarrierBookings.Report;

public sealed class ReportCarrierCancellationNotDeliveredHandler(IRepository<CarrierBooking> carrierBookings, TimeProvider time)
    : ICommandHandler<ReportCarrierCancellationNotDeliveredCommand, Result<CarrierBookingReportResult>>
{
    public async ValueTask<Result<CarrierBookingReportResult>> Handle(ReportCarrierCancellationNotDeliveredCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (booking, refusal) = await CarrierBookingLookup.ToCancelledBookingAsync(carrierBookings, command.CarrierBookingNumber, cancellationToken);
        if (booking is null)
        {
            return CarrierBookingRefused.Because<CarrierBookingReportResult>(
                refusal ?? throw new InvalidOperationException($"Carrier booking {command.CarrierBookingNumber} was neither found nor refused."));
        }

        if (!booking.ReportCarrierNotTold(command.StatusCode, command.Message, new CorrelationId(command.CorrelationId), time.GetUtcNow()))
        {
            // Already reported told or not told: ignored, changing and publishing nothing.
            return new CarrierBookingReportResult(booking.Number.Value, booking.Status, Ignored: true);
        }

        await carrierBookings.SaveChangesAsync(cancellationToken);
        return new CarrierBookingReportResult(booking.Number.Value, booking.Status);
    }
}

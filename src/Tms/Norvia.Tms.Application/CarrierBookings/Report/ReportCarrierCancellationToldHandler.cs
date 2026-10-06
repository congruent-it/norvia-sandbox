using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

public sealed class ReportCarrierCancellationToldHandler(IRepository<CarrierBooking> carrierBookings, TimeProvider time)
    : ICommandHandler<ReportCarrierCancellationToldCommand, Result<CarrierBookingReportResult>>
{
    public async ValueTask<Result<CarrierBookingReportResult>> Handle(ReportCarrierCancellationToldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (booking, refusal) = await CarrierBookingLookup.ToCancelledBookingAsync(carrierBookings, command.CarrierBookingNumber, cancellationToken);
        if (booking is null)
        {
            return CarrierBookingRefused.Because<CarrierBookingReportResult>(
                refusal ?? throw new InvalidOperationException($"Carrier booking {command.CarrierBookingNumber} was neither found nor refused."));
        }

        if (!booking.MarkCarrierTold(time.GetUtcNow()))
        {
            // Already reported told or not told: ignored, changing nothing.
            return new CarrierBookingReportResult(booking.Number.Value, booking.Status, Ignored: true);
        }

        await carrierBookings.SaveChangesAsync(cancellationToken);
        return new CarrierBookingReportResult(booking.Number.Value, booking.Status);
    }
}

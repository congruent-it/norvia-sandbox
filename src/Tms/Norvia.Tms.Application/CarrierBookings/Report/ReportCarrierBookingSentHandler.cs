using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

public sealed class ReportCarrierBookingSentHandler(IRepository<CarrierBooking> carrierBookings, TimeProvider time)
    : ICommandHandler<ReportCarrierBookingSentCommand, Result<CarrierBookingReportResult>>
{
    public async ValueTask<Result<CarrierBookingReportResult>> Handle(ReportCarrierBookingSentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (booking, refusal) = await CarrierBookingLookup.ToBookingAsync(carrierBookings, command.CarrierBookingNumber, cancellationToken);
        if (refusal is not null)
        {
            return CarrierBookingRefused.Because<CarrierBookingReportResult>(refusal);
        }

        if (!booking!.MarkSent(time.GetUtcNow()))
        {
            // No longer Requested, for example withdrawn while the report was in flight: ignored, changing nothing.
            return new CarrierBookingReportResult(booking.Number.Value, booking.Status, Ignored: true);
        }

        await carrierBookings.SaveChangesAsync(cancellationToken);
        return new CarrierBookingReportResult(booking.Number.Value, booking.Status);
    }
}

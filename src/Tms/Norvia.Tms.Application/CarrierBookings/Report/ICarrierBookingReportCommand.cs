namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>What a report command names for tracking (coding guidelines §7): the booking and the trail it continues.</summary>
public interface ICarrierBookingReportCommand
{
    string CarrierBookingNumber { get; }

    string CorrelationId { get; }
}

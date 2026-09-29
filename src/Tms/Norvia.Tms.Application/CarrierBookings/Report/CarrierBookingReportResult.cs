using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// What reporting a Carrier booking as sent or not delivered changed, or that the report was ignored because the
/// booking was no longer Requested (P4).
/// </summary>
public sealed record CarrierBookingReportResult(string CarrierBookingNumber, CarrierBookingStatus Status, bool Ignored = false);

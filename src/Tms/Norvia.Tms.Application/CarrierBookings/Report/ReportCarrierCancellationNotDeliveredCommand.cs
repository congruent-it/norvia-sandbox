using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// The Carrier cancellation Logic App reports that the cancellation or withdrawal message for a Cancelled or Withdrawn Carrier
/// booking never reached the Carrier, with its technical cause (P7): the TMS flags the booking Carrier not told and publishes
/// CarrierCancellationNotDelivered. A report for a booking that has already been reported is ignored: it changes and publishes nothing.
/// </summary>
/// <param name="CorrelationId">The cancellation's or withdrawal's own trail, which the report continues.</param>
/// <param name="StatusCode">The Carrier endpoint's HTTP status code, or the technical failure's own code.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ReportCarrierCancellationNotDeliveredCommand(string CarrierBookingNumber, string CorrelationId, int StatusCode, string Message)
    : ICommand<Result<CarrierBookingReportResult>>, ICarrierBookingReportCommand;

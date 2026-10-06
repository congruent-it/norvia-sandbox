using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// The Carrier cancellation Logic App reports that the cancellation or withdrawal message for a Cancelled or Withdrawn Carrier
/// booking reached the Carrier (P7): the TMS records the Carrier told time and publishes nothing. A report for a booking that has
/// already been reported is ignored: it changes nothing.
/// </summary>
/// <param name="CorrelationId">The cancellation's or withdrawal's own trail, which the report continues.</param>
public sealed record ReportCarrierCancellationToldCommand(string CarrierBookingNumber, string CorrelationId)
    : ICommand<Result<CarrierBookingReportResult>>, ICarrierBookingReportCommand;

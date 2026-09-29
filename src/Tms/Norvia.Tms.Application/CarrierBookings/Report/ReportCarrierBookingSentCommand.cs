using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// The P4 booking Logic App reports that a Requested Carrier booking's request reached the Carrier: the TMS records the
/// sent time and publishes nothing. A report for a booking that is no longer Requested (for example, withdrawn while the
/// report was in flight) is ignored: it changes nothing.
/// </summary>
/// <param name="CorrelationId">The release's own trail, which the report continues.</param>
public sealed record ReportCarrierBookingSentCommand(string CarrierBookingNumber, string CorrelationId)
    : ICommand<Result<CarrierBookingReportResult>>, ICarrierBookingReportCommand;

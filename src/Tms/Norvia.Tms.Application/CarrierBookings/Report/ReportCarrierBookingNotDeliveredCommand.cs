using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// The P4 booking Logic App reports that a Requested Carrier booking's request never reached the Carrier, with its
/// technical cause: the booking becomes NotDelivered, the Shipment goes back to Planned, and CarrierBookingNotDelivered
/// is published, in one transaction. A report for a booking that is no longer Requested (for example, withdrawn while the
/// report was in flight) is ignored: it changes and publishes nothing.
/// </summary>
/// <param name="CorrelationId">The release's own trail, which the report continues.</param>
/// <param name="StatusCode">The Carrier endpoint's HTTP status code, or the technical failure's own code.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ReportCarrierBookingNotDeliveredCommand(string CarrierBookingNumber, string CorrelationId, int StatusCode, string Message)
    : ICommand<Result<CarrierBookingReportResult>>, ICarrierBookingReportCommand;

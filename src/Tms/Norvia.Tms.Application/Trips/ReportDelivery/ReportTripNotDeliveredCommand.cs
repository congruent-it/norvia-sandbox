using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

/// <summary>
/// The Trip dispatch Logic App reports that it gave up sending this version of the Trip to the Driver app, with the technical cause (P11):
/// the TMS records it and, for the Trip's latest version, publishes TripNotDelivered.
/// </summary>
/// <param name="CorrelationId">The Dispatch's own trail, or the send-again's, which the report continues.</param>
/// <param name="Cause">The technical cause in plain language.</param>
public sealed record ReportTripNotDeliveredCommand(string Number, int Version, string CorrelationId, string Cause)
    : ICommand<Result<TripDeliveryReportResult>>, ITripDeliveryReportCommand;

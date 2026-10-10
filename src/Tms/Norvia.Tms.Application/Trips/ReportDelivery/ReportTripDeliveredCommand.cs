using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

/// <summary>The Trip dispatch Logic App reports that the Driver app accepted this version of the Trip (P11).</summary>
/// <param name="CorrelationId">The Dispatch's own trail, or the send-again's, which the report continues.</param>
public sealed record ReportTripDeliveredCommand(string Number, int Version, string CorrelationId)
    : ICommand<Result<TripDeliveryReportResult>>, ITripDeliveryReportCommand;

using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Application.TransportOrders.Assign;

/// <summary>
/// Traces a Planner's assignment on its own Message trail, as <see cref="TrackPlanningInLoggingScope{TCommand, TOutcome}"/>
/// does, with the Transport Order number for the whole call, as the call names no Shipment yet.
/// </summary>
public sealed partial class TrackAssignmentInLoggingScope(ILogger<TrackAssignmentInLoggingScope> logger)
    : TrackPlanningInLoggingScope<AssignTransportOrderCommand, AssignTransportOrderResult>(logger, PlanningAction.Assignment)
{
    protected override (string CorrelationId, string NumberKey, string Number) TrailOf(AssignTransportOrderCommand message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return (message.CorrelationId, TrackingKeys.TransportOrderNumber, message.TransportOrderNumber);
    }

    protected override void LogDone(AssignTransportOrderResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var carriedBy = NameOf(outcome.CarriedBy);
        LogPlanned(Logger, outcome.TransportOrderNumber, outcome.ShipmentNumber, carriedBy);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} planned as Shipment {ShipmentNumber}, carried by {CarriedBy}")]
    private static partial void LogPlanned(ILogger logger, string transportOrderNumber, string shipmentNumber, string carriedBy);
}

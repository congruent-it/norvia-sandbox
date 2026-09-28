using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Shipments.Reassign;

/// <summary>
/// Traces a Planner's reassignment on its own Message trail, as <see cref="TrackPlanningInLoggingScope{TCommand, TOutcome}"/>
/// does, with the Shipment number for the whole call.
/// </summary>
public sealed partial class TrackReassignmentInLoggingScope(ILogger<TrackReassignmentInLoggingScope> logger)
    : TrackPlanningInLoggingScope<ReassignShipmentCommand, ReassignShipmentResult>(logger, "Reassignment")
{
    protected override (string CorrelationId, string NumberKey, string Number) TrailOf(ReassignShipmentCommand message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return (message.CorrelationId, TrackingKeys.ShipmentNumber, message.ShipmentNumber);
    }

    protected override void LogDone(ReassignShipmentResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var previouslyCarriedBy = NameOf(outcome.PreviouslyCarriedBy);
        var carriedBy = NameOf(outcome.CarriedBy);
        LogReassigned(Logger, outcome.ShipmentNumber, outcome.TransportOrderNumber, previouslyCarriedBy, carriedBy);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipment {ShipmentNumber} of Transport Order {TransportOrderNumber} reassigned from {PreviouslyCarriedBy} to {CarriedBy}")]
    private static partial void LogReassigned(ILogger logger, string shipmentNumber, string transportOrderNumber, string previouslyCarriedBy, string carriedBy);
}

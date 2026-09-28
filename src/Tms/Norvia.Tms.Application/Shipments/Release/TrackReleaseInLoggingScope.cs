using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Shipments.Release;

/// <summary>
/// Traces a Planner's release on its own Message trail, as <see cref="TrackPlanningInLoggingScope{TCommand, TOutcome}"/>
/// does, with the Shipment number for the whole call.
/// </summary>
public sealed partial class TrackReleaseInLoggingScope(ILogger<TrackReleaseInLoggingScope> logger)
    : TrackPlanningInLoggingScope<ReleaseShipmentCommand, ReleaseShipmentResult>(logger, "Release")
{
    protected override (string CorrelationId, string NumberKey, string Number) TrailOf(ReleaseShipmentCommand message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return (message.CorrelationId, TrackingKeys.ShipmentNumber, message.ShipmentNumber);
    }

    protected override void LogDone(ReleaseShipmentResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var carriedBy = NameOf(outcome.CarriedBy);
        LogReleased(Logger, outcome.ShipmentNumber, outcome.TransportOrderNumber, carriedBy);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipment {ShipmentNumber} of Transport Order {TransportOrderNumber} released to {CarriedBy}")]
    private static partial void LogReleased(ILogger logger, string shipmentNumber, string transportOrderNumber, string carriedBy);
}

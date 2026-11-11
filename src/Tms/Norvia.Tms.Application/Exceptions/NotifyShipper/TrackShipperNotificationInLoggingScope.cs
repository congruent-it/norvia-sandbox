using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Exceptions.NotifyShipper;

/// <summary>Traces the Planner's notifying the Shipper on its own Message trail (<see cref="TrackExceptionActionInLoggingScope{TCommand, TResult}"/>).</summary>
public sealed partial class TrackShipperNotificationInLoggingScope(ILogger<TrackShipperNotificationInLoggingScope> logger)
    : TrackExceptionActionInLoggingScope<NotifyShipperCommand, NotifyShipperResult>
{
    protected override ILogger Logger => logger;

    // Only a notification sent from an Agent's draft names the Agent and its run (ADR 0040), edited or not.
    protected override Dictionary<string, object?> ExtraKeysOf(NotifyShipperCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.ActingOn is { } run
            ? new() { [TrackingKeys.AgentId] = run.AgentName, [TrackingKeys.AgentRunId] = run.RunId }
            : [];
    }

    protected override string CorrelationIdOf(NotifyShipperCommand command) => command.CorrelationId;

    protected override string ShipmentNumberOf(NotifyShipperResult result) => result.ShipmentNumber;

    protected override void LogSucceeded(NotifyShipperResult result) => LogRequested(logger, result.NotificationId, result.ExceptionNumber, result.ShipmentNumber);

    protected override void LogRefused(NotifyShipperCommand command, string reason) => LogRefused(logger, command.ExceptionNumber, reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper notification {NotificationId} requested from Exception {ExceptionNumber} of Shipment {ShipmentNumber}")]
    private static partial void LogRequested(ILogger logger, string notificationId, string exceptionNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notifying the Shipper from Exception {ExceptionNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string exceptionNumber, string reason);
}

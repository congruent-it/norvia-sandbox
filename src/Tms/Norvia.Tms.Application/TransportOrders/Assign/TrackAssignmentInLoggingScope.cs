using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Application.TransportOrders.Assign;

/// <summary>
/// Traces a Planner's assignment on its own Message trail (coding guidelines §7): the <c>correlationId</c> the TMS
/// created for it, with <c>partyId</c>, <c>transportOrderNumber</c>, <c>shipmentNumber</c> and <c>outcome</c>, in the
/// logging scope and on the request's trace, so the assignment is found by its Transport Order number. One the TMS
/// refuses records Refused. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackAssignmentInLoggingScope(ILogger<TrackAssignmentInLoggingScope> logger)
    : IPipelineBehavior<AssignTransportOrderCommand, Result<AssignTransportOrderResult>>
{
    public async ValueTask<Result<AssignTransportOrderResult>> Handle(
        AssignTransportOrderCommand message,
        MessageHandlerDelegate<AssignTransportOrderCommand, Result<AssignTransportOrderResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var numbered = Track(new() { [TrackingKeys.TransportOrderNumber] = message.TransportOrderNumber });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(result.Value);
        }
        else
        {
            using var refused = Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused });
            LogRefused(logger, result.Errors);
        }

        return result;
    }

    private void LogOutcome(AssignTransportOrderResult outcome)
    {
        using var scope = Track(new()
        {
            [TrackingKeys.CorrelationId] = outcome.CorrelationId,
            [TrackingKeys.PartyId] = outcome.ShipperId,
            [TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        });
        var carriedBy = outcome.CarriedBy.Carrier?.Id ?? "Own fleet";
        if (outcome.Repeat)
        {
            LogRepeat(logger, outcome.TransportOrderNumber, outcome.ShipmentNumber);
        }
        else
        {
            LogPlanned(logger, outcome.TransportOrderNumber, outcome.ShipmentNumber, carriedBy);
        }
    }

    private IDisposable? Track(Dictionary<string, object?> keys)
    {
        foreach (var (key, value) in keys)
        {
            Activity.Current?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} planned as Shipment {ShipmentNumber}, carried by {CarriedBy}")]
    private static partial void LogPlanned(ILogger logger, string transportOrderNumber, string shipmentNumber, string carriedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} assignment answered as a repeat: Shipment {ShipmentNumber}")]
    private static partial void LogRepeat(ILogger logger, string transportOrderNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Assignment refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, IEnumerable<string> reason);
}

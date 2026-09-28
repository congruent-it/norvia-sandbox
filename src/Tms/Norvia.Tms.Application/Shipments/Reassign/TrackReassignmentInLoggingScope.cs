using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Shipments.Reassign;

/// <summary>
/// Traces a Planner's reassignment on its own Message trail (coding guidelines §7): the <c>correlationId</c> the TMS
/// created for it and <c>shipmentNumber</c> for the whole call, refused ones included, and <c>partyId</c>,
/// <c>transportOrderNumber</c> and <c>outcome</c> for its outcome, in the logging scope and on the request's trace, so the
/// reassignment is found by its Transport Order number too. One the TMS refuses records Refused, with its
/// <c>failureCode</c>. A repeat's outcome is traced on the trail of the reassignment it repeats. The API's request scope
/// carries <c>flow</c>.
/// </summary>
public sealed partial class TrackReassignmentInLoggingScope(ILogger<TrackReassignmentInLoggingScope> logger)
    : IPipelineBehavior<ReassignShipmentCommand, Result<ReassignShipmentResult>>
{
    public async ValueTask<Result<ReassignShipmentResult>> Handle(
        ReassignShipmentCommand message,
        MessageHandlerDelegate<ReassignShipmentCommand, Result<ReassignShipmentResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = Track(new()
        {
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.ShipmentNumber] = message.ShipmentNumber,
        });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(message, result.Value);
        }
        else
        {
            LogRefusal(result);
        }

        return result;
    }

    private void LogOutcome(ReassignShipmentCommand message, ReassignShipmentResult outcome)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.PartyId] = outcome.ShipperId,
            [TrackingKeys.TransportOrderNumber] = outcome.TransportOrderNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        };
        if (outcome.CorrelationId != message.CorrelationId)
        {
            // A repeat answers on the trail of the reassignment it repeats.
            keys[TrackingKeys.CorrelationId] = outcome.CorrelationId;
        }

        using var scope = Track(keys);
        var previouslyCarriedBy = NameOf(outcome.PreviouslyCarriedBy);
        var carriedBy = NameOf(outcome.CarriedBy);
        if (outcome.Repeat)
        {
            LogRepeat(logger, outcome.ShipmentNumber);
        }
        else
        {
            LogReassigned(logger, outcome.ShipmentNumber, outcome.TransportOrderNumber, previouslyCarriedBy, carriedBy);
        }
    }

    private void LogRefusal(Result<ReassignShipmentResult> result)
    {
        var refusal = result.ValidationErrors.First();
        using var refused = Track(new()
        {
            [TrackingKeys.Outcome] = TrackingKeys.Refused,
            [TrackingKeys.FailureCode] = refusal.ErrorCode,
        });
        LogRefused(logger, refusal.ErrorCode, refusal.ErrorMessage);
    }

    private IDisposable? Track(Dictionary<string, object?> keys)
    {
        foreach (var (key, value) in keys)
        {
            Activity.Current?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }

    private static string NameOf(CarriedByView carriedBy) => carriedBy.Carrier?.Id ?? "Own fleet";

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipment {ShipmentNumber} of Transport Order {TransportOrderNumber} reassigned from {PreviouslyCarriedBy} to {CarriedBy}")]
    private static partial void LogReassigned(ILogger logger, string shipmentNumber, string transportOrderNumber, string previouslyCarriedBy, string carriedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipment {ShipmentNumber} reassignment answered as a repeat")]
    private static partial void LogRepeat(ILogger logger, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reassignment refused with {FailureCode}: {Reason}")]
    private static partial void LogRefused(ILogger logger, string failureCode, string reason);
}

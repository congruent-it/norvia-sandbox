using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments;

/// <summary>
/// Traces a Planner's action (an assignment, reassignment or release) on its own Message trail (coding guidelines §7):
/// the <c>correlationId</c> the TMS created for it and the number the call names for the whole call, refused ones
/// included, and <c>partyId</c>, <c>transportOrderNumber</c>, <c>shipmentNumber</c> and <c>outcome</c> for its outcome, in
/// the logging scope and on the request's trace, so the action is found by its Transport Order number. One the TMS
/// refuses records Refused, with its <c>failureCode</c>. A repeat's outcome is traced on the trail of the action it
/// repeats. The API's request scope carries <c>flow</c>. Each action says only how its outcome reads.
/// </summary>
/// <param name="action">The action, which the repeat and refusal messages name.</param>
public abstract partial class TrackPlanningInLoggingScope<TCommand, TOutcome>(ILogger logger, PlanningAction action)
    : IPipelineBehavior<TCommand, Result<TOutcome>>
    where TCommand : IMessage
    where TOutcome : IPlanningOutcome
{
    protected ILogger Logger { get; } = logger;

    public async ValueTask<Result<TOutcome>> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, Result<TOutcome>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        var (correlationId, numberKey, number) = TrailOf(message);
        using var trail = Logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [numberKey] = number,
        });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(correlationId, numberKey, result.Value);
        }
        else
        {
            LogRefusal(result);
        }

        return result;
    }

    /// <summary>
    /// The call's own trail, and the number it names with its tracking key, such as <c>shipmentNumber</c>, traced even
    /// when the action is refused.
    /// </summary>
    protected abstract (string CorrelationId, string NumberKey, string Number) TrailOf(TCommand message);

    /// <summary>Logs what the action did, for an outcome that isn't a repeat.</summary>
    protected abstract void LogDone(TOutcome outcome);

    /// <summary>Who carries the Shipment, as the outcome messages name it: the Carrier's ID, or Own fleet.</summary>
    protected static string NameOf(CarriedByView carriedBy)
    {
        ArgumentNullException.ThrowIfNull(carriedBy);
        return carriedBy.Carrier?.Id ?? "Own fleet";
    }

    private void LogOutcome(string correlationId, string numberKey, TOutcome outcome)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.PartyId] = outcome.ShipperId,
            [TrackingKeys.TransportOrderNumber] = outcome.TransportOrderNumber,
            [TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        };
        keys.Remove(numberKey); // The call's scope carries it already.
        if (outcome.CorrelationId != correlationId)
        {
            // A repeat answers on the trail of the action it repeats.
            keys[TrackingKeys.CorrelationId] = outcome.CorrelationId;
        }

        using var scope = Logger.Track(keys);
        if (outcome.Repeat)
        {
            LogRepeat(Logger, action, outcome.TransportOrderNumber, outcome.ShipmentNumber);
        }
        else
        {
            LogDone(outcome);
        }
    }

    private void LogRefusal(Result<TOutcome> result)
    {
        var refusal = result.ValidationErrors.First();
        using var refused = Logger.Track(new()
        {
            [TrackingKeys.Outcome] = TrackingKeys.Refused,
            [TrackingKeys.FailureCode] = refusal.ErrorCode,
        });
        LogRefused(Logger, action, refusal.ErrorCode, refusal.ErrorMessage);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} of Transport Order {TransportOrderNumber}, Shipment {ShipmentNumber}, answered as a repeat")]
    private static partial void LogRepeat(ILogger logger, PlanningAction action, string transportOrderNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} refused with {FailureCode}: {Reason}")]
    private static partial void LogRefused(ILogger logger, PlanningAction action, string failureCode, string reason);
}

using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.CarrierBookings.Withdraw;

/// <summary>
/// Traces the Planner's withdrawal on its own Message trail (coding guidelines §7): the <c>correlationId</c> the TMS
/// created for it and the <c>carrierBookingNumber</c> the call names, refused ones included, and
/// <c>shipmentNumber</c> and <c>outcome</c> for its outcome, in the logging scope and on the request's trace. One the
/// TMS refuses records Refused, with its <c>failureCode</c> where the refusal has one. A repeat's outcome is traced on
/// the trail of the withdrawal it repeats. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackCarrierBookingWithdrawalInLoggingScope(ILogger<TrackCarrierBookingWithdrawalInLoggingScope> logger)
    : IPipelineBehavior<WithdrawCarrierBookingCommand, Result<CarrierBookingWithdrawResult>>
{
    public async ValueTask<Result<CarrierBookingWithdrawResult>> Handle(
        WithdrawCarrierBookingCommand message,
        MessageHandlerDelegate<WithdrawCarrierBookingCommand, Result<CarrierBookingWithdrawResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = Track(new()
        {
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.CarrierBookingNumber] = message.CarrierBookingNumber,
        });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(message.CorrelationId, result.Value);
        }
        else
        {
            LogRefusal(result);
        }

        return result;
    }

    private void LogOutcome(string correlationId, CarrierBookingWithdrawResult outcome)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        };
        if (outcome.CorrelationId != correlationId)
        {
            // A repeat answers on the trail of the withdrawal it repeats.
            keys[TrackingKeys.CorrelationId] = outcome.CorrelationId;
        }

        using var scope = Track(keys);
        if (outcome.Repeat)
        {
            LogRepeat(logger, outcome.CarrierBookingNumber, outcome.Status);
        }
        else
        {
            LogWithdrawn(logger, outcome.CarrierBookingNumber, outcome.ShipmentNumber);
        }
    }

    private void LogRefusal(Result<CarrierBookingWithdrawResult> result)
    {
        var refusal = result.ValidationErrors.First();
        var keys = new Dictionary<string, object?> { [TrackingKeys.Outcome] = TrackingKeys.Refused };
        if (!string.IsNullOrEmpty(refusal.ErrorCode))
        {
            keys[TrackingKeys.FailureCode] = refusal.ErrorCode;
        }

        using var refused = Track(keys);
        LogRefused(logger, refusal.ErrorMessage);
    }

    private IDisposable? Track(Dictionary<string, object?> keys)
    {
        foreach (var (key, value) in keys)
        {
            Activity.Current?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} withdrawn, Shipment {ShipmentNumber} back to Planned")]
    private static partial void LogWithdrawn(ILogger logger, string carrierBookingNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} withdrawal answered as a repeat: {Status}")]
    private static partial void LogRepeat(ILogger logger, string carrierBookingNumber, CarrierBookingStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "The withdrawal refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}

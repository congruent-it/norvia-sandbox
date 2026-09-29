using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>
/// Traces the Carrier's answer on its own Message trail (coding guidelines §7): the answer's own <c>correlationId</c>,
/// <c>partyId</c> from the Carrier the command names, <c>carrierBookingNumber</c> for the whole call, and
/// <c>shipmentNumber</c> and <c>outcome</c> for its outcome, in the logging scope and on the request's trace. One the
/// TMS refuses records Refused, with its <c>failureCode</c> where the refusal has one (the wrong-Carrier rule doesn't).
/// A repeat's outcome is traced on the trail of the answer it repeats. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackCarrierBookingAnswerInLoggingScope<TCommand>(ILogger<TrackCarrierBookingAnswerInLoggingScope<TCommand>> logger)
    : IPipelineBehavior<TCommand, Result<CarrierBookingAnswerResult>>
    where TCommand : IMessage, ICarrierBookingAnswerCommand
{
    public async ValueTask<Result<CarrierBookingAnswerResult>> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, Result<CarrierBookingAnswerResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = Track(new()
        {
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.CarrierBookingNumber] = message.CarrierBookingNumber,
            [TrackingKeys.PartyId] = message.CarrierId,
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

    private void LogOutcome(string correlationId, CarrierBookingAnswerResult outcome)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        };
        if (outcome.CorrelationId != correlationId)
        {
            // A repeat answers on the trail of the answer it repeats.
            keys[TrackingKeys.CorrelationId] = outcome.CorrelationId;
        }

        using var scope = Track(keys);
        if (outcome.Repeat)
        {
            LogRepeat(logger, outcome.CarrierBookingNumber, outcome.Status);
        }
        else
        {
            LogAnswered(logger, outcome.CarrierBookingNumber, outcome.Status, outcome.ShipmentNumber, outcome.ShipmentStatus);
        }
    }

    private void LogRefusal(Result<CarrierBookingAnswerResult> result)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} answered: {Status}, Shipment {ShipmentNumber} {ShipmentStatus}")]
    private static partial void LogAnswered(ILogger logger, string carrierBookingNumber, CarrierBookingStatus status, string shipmentNumber, ShipmentStatus shipmentStatus);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} answered as a repeat: {Status}")]
    private static partial void LogRepeat(ILogger logger, string carrierBookingNumber, CarrierBookingStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Carrier's answer refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}

using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

/// <summary>
/// Puts the tracking-contract keys the TMS knows about a submission in the logging scope, so every trace of an intake
/// call joins its Message trail (coding guidelines §7). The Transport Order number is known only once the booking is
/// accepted, so it's added for the outcome. Only keys and rejection codes are logged, never what the Shipper submitted:
/// that holds the Consignee's name, address and phone number.
/// </summary>
public sealed partial class TrackIntakeInLoggingScope(ILogger<TrackIntakeInLoggingScope> logger)
    : IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>
{
    public async ValueTask<Result<SubmitTransportOrderResult>> Handle(
        SubmitTransportOrderCommand message,
        MessageHandlerDelegate<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.BeginScope(TrailOf(message));
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(result.Value);
        }

        return result;
    }

    private static Dictionary<string, object?> TrailOf(SubmitTransportOrderCommand message)
    {
        var trail = new Dictionary<string, object?>
        {
            ["correlationId"] = message.CorrelationId,
            ["flow"] = Flows.TransportOrderIntake,
        };
        if (!string.IsNullOrWhiteSpace(message.TransportOrder.ShipperId))
        {
            trail["partyId"] = message.TransportOrder.ShipperId;
        }

        return trail;
    }

    private void LogOutcome(SubmitTransportOrderResult outcome)
    {
        if (outcome.TransportOrderNumber is not { } number)
        {
            var rejectionCodes = string.Join(", ", outcome.RejectionReasons.Select(reason => reason.Code));
            LogRejected(logger, rejectionCodes);
            return;
        }

        using var numbered = logger.BeginScope(new Dictionary<string, object?> { ["transportOrderNumber"] = number });
        if (outcome.Repeat)
        {
            LogRepeat(logger, number);
        }
        else
        {
            LogAccepted(logger, number, outcome.Status);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} {Status}")]
    private static partial void LogAccepted(ILogger logger, string transportOrderNumber, TransportOrderStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} answered as a repeat of the original booking")]
    private static partial void LogRepeat(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Booking rejected: {RejectionCodes}")]
    private static partial void LogRejected(ILogger logger, string rejectionCodes);
}

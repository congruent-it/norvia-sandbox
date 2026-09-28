using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

/// <summary>
/// Adds the tracking-contract keys only the submission knows to the logging scope (coding guidelines §7): the Shipper
/// as <c>partyId</c>, and for the outcome the Transport Order number once the booking is accepted, and <c>outcome</c>:
/// Succeeded for an accepted booking or a repeat, Refused with the <c>failureCode</c> <c>TransportOrderRejected</c> for a
/// Rejected booking, as the intake's other hops record the TMS's answer. The API's request scope already carries
/// <c>correlationId</c> and <c>flow</c> for the whole intake call, refused calls included. Only keys and rejection codes
/// are logged, never what the Shipper submitted: that holds the Consignee's name, address and phone number.
/// </summary>
public sealed partial class TrackIntakeInLoggingScope(ILogger<TrackIntakeInLoggingScope> logger)
    : IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>
{
    /// <summary>The failure code of a Rejected booking; its rejection reasons say why.</summary>
    private const string TransportOrderRejected = "TransportOrderRejected";

    public async ValueTask<Result<SubmitTransportOrderResult>> Handle(
        SubmitTransportOrderCommand message,
        MessageHandlerDelegate<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var party = logger.BeginScope(PartyOf(message));
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(result.Value);
        }

        return result;
    }

    private static Dictionary<string, object?> PartyOf(SubmitTransportOrderCommand message) =>
        string.IsNullOrWhiteSpace(message.TransportOrder.ShipperId)
            ? []
            : new() { [TrackingKeys.PartyId] = message.TransportOrder.ShipperId };

    private void LogOutcome(SubmitTransportOrderResult outcome)
    {
        if (outcome.TransportOrderNumber is not { } number)
        {
            var rejectionCodes = string.Join(", ", outcome.RejectionReasons.Select(reason => reason.Code));
            using var refused = logger.BeginScope(new Dictionary<string, object?>
            {
                [TrackingKeys.Outcome] = TrackingKeys.Refused,
                [TrackingKeys.FailureCode] = TransportOrderRejected,
            });
            LogRejected(logger, rejectionCodes);
            return;
        }

        using var numbered = logger.BeginScope(new Dictionary<string, object?>
        {
            [TrackingKeys.TransportOrderNumber] = number,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        });
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

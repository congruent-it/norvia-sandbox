using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

/// <summary>
/// Traces a sent or not-delivered report on the trail it continues, the release's (coding guidelines §7): its
/// <c>correlationId</c> and <c>carrierBookingNumber</c> for the whole call, and <c>outcome</c> for its outcome, in the
/// logging scope and on the request's trace. A report the TMS ignores, because the booking is no longer Requested, still
/// records Succeeded: it isn't a refusal, only a no-op. One the TMS refuses (an unknown booking number) records Refused,
/// with its <c>failureCode</c>. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackCarrierBookingReportInLoggingScope<TCommand>(ILogger<TrackCarrierBookingReportInLoggingScope<TCommand>> logger)
    : IPipelineBehavior<TCommand, Result<CarrierBookingReportResult>>
    where TCommand : IMessage, ICarrierBookingReportCommand
{
    public async ValueTask<Result<CarrierBookingReportResult>> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, Result<CarrierBookingReportResult>> next,
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
            LogOutcome(result.Value);
        }
        else
        {
            LogRefusal(result);
        }

        return result;
    }

    private void LogOutcome(CarrierBookingReportResult outcome)
    {
        using var scope = Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        if (outcome.Ignored)
        {
            LogIgnored(logger, outcome.CarrierBookingNumber, outcome.Status);
        }
        else
        {
            LogRecorded(logger, outcome.CarrierBookingNumber, outcome.Status);
        }
    }

    private void LogRefusal(Result<CarrierBookingReportResult> result)
    {
        var refusal = result.ValidationErrors.First();
        using var refused = Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} report recorded: {Status}")]
    private static partial void LogRecorded(ILogger logger, string carrierBookingNumber, CarrierBookingStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} report ignored, as it is {Status}, not Requested")]
    private static partial void LogIgnored(ILogger logger, string carrierBookingNumber, CarrierBookingStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Carrier booking report refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}

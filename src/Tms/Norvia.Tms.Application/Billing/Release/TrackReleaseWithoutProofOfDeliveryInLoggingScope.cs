using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.Exceptions;

namespace Norvia.Tms.Application.Billing.Release;

/// <summary>Traces Customer service's release without a Proof of delivery on its own Message trail (<see cref="TrackExceptionActionInLoggingScope{TCommand, TResult}"/>).</summary>
public sealed partial class TrackReleaseWithoutProofOfDeliveryInLoggingScope(ILogger<TrackReleaseWithoutProofOfDeliveryInLoggingScope> logger)
    : TrackExceptionActionInLoggingScope<ReleaseWithoutProofOfDeliveryCommand, ReleaseWithoutProofOfDeliveryResult>
{
    protected override ILogger Logger => logger;

    protected override string CorrelationIdOf(ReleaseWithoutProofOfDeliveryCommand command) => command.CorrelationId;

    protected override string ShipmentNumberOf(ReleaseWithoutProofOfDeliveryResult result) => result.ShipmentNumber;

    protected override void LogSucceeded(ReleaseWithoutProofOfDeliveryResult result) => LogReleased(logger, result.TransportOrderNumber, result.BillingState);

    protected override void LogRefused(ReleaseWithoutProofOfDeliveryCommand command, string reason) => LogRefused(logger, command.TransportOrderNumber, reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} released without a Proof of delivery, now {BillingState}")]
    private static partial void LogReleased(ILogger logger, string transportOrderNumber, Norvia.Tms.Domain.TransportOrders.BillingState billingState);

    [LoggerMessage(Level = LogLevel.Information, Message = "Release of Transport Order {TransportOrderNumber} without a Proof of delivery refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string transportOrderNumber, string reason);
}

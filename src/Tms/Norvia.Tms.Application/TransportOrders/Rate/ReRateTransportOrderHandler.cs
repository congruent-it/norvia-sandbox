using System.Diagnostics;
using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Re-rates a Transport Order whose Rating failed. It adds the keys only it knows to the logging scope and the trace
/// (coding guidelines §7): the Shipper as <c>partyId</c>, and <c>failureCode</c> when Rating fails again. The API's
/// request scope carries <c>correlationId</c>, <c>flow</c> and <c>transportOrderNumber</c>.
/// </summary>
public sealed partial class ReRateTransportOrderHandler(
    IRepository<TransportOrder> repository,
    IRateCardDirectory rateCards,
    TimeProvider time,
    ILogger<ReRateTransportOrderHandler> logger)
    : ICommandHandler<ReRateTransportOrderCommand, Result<RateTransportOrderResult>>
{
    public async ValueTask<Result<RateTransportOrderResult>> Handle(ReRateTransportOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A Rejected booking has no number and no rating result, so it is never found to re-rate.
        if (!TransportOrderNumber.TryParse(command.TransportOrderNumber, out var number)
            || await repository.SingleOrDefaultAsync(new TransportOrderToRateSpec(number), cancellationToken) is not { RatingResult: { } } order)
        {
            return Result.NotFound();
        }

        Activity.Current?.SetTag(TrackingKeys.PartyId, order.ShipperId.Value);
        using var party = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.PartyId] = order.ShipperId.Value });

        if (!order.ReRate(rateCards.FindFor(order.ShipperId), time.GetUtcNow(), new CorrelationId(command.CorrelationId)))
        {
            LogRefused(logger, number.Value, order.RatingResult.Value);
            return Result.Conflict(
                $"Transport Order {number} is {order.RatingResult}; only a Transport Order whose Rating failed can be re-rated.");
        }

        // The new rating result and the outcome event's outbox message are saved in one transaction.
        await repository.SaveChangesAsync(cancellationToken);

        var result = new RateTransportOrderResult(order.RatingResult.Value, RatingFailureView.From(order.RatingFailure));
        if (result.RatingFailure is { } failure)
        {
            Activity.Current?.SetTag(TrackingKeys.FailureCode, failure.Code);
            using var failed = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.FailureCode] = failure.Code });
            LogFailedAgain(logger, number.Value, failure.Code, failure.Message);
        }
        else
        {
            LogReRated(logger, number.Value);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} re-rated: Rated")]
    private static partial void LogReRated(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transport Order {TransportOrderNumber} re-rated: Rating failed again with {RatingFailureCode}: {RatingFailureMessage}")]
    private static partial void LogFailedAgain(ILogger logger, string transportOrderNumber, string ratingFailureCode, string ratingFailureMessage);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-rating Transport Order {TransportOrderNumber} refused: it is {RatingResult}")]
    private static partial void LogRefused(ILogger logger, string transportOrderNumber, RatingResult ratingResult);
}

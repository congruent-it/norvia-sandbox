using Mediator;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Infrastructure.Data;
using Norvia.Tms.Infrastructure.Data.Configurations;

namespace Norvia.Tms.Infrastructure.Trips;

/// <summary>
/// When two requests ask at once for the same Vehicle or Driver on a day, or the same request arrives twice, both find no
/// Trip holding it and the unique indexes let only one be saved. The other is run once more: by then the winner is
/// committed, so it is refused with <c>VehicleNotAvailable</c> or <c>DriverNotAvailable</c>, or answered with the Draft the
/// first one created, instead of failing.
/// </summary>
internal sealed class RetryTripThatLostARace<TCommand, TResponse>(TmsDbContext dbContext) : IPipelineBehavior<TCommand, TResponse>
    where TCommand : IMessage
{
    private const int DuplicateKeyInUniqueIndex = 2601;

    private static readonly string[] Indexes = [TripConfiguration.VehicleDayIndex, TripConfiguration.DriverDayIndex, TripConfiguration.IdempotencyKeyIndex];

    public async ValueTask<TResponse> Handle(TCommand message, MessageHandlerDelegate<TCommand, TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(message, cancellationToken);
        }
        catch (DbUpdateException exception) when (LostARace(exception))
        {
            // The failed attempt's Trip is still tracked; it may not be saved.
            dbContext.ChangeTracker.Clear();
            return await next(message, cancellationToken);
        }
    }

    private static bool LostARace(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateKeyInUniqueIndex } sqlException
        && Indexes.Any(index => sqlException.Message.Contains(index, StringComparison.Ordinal));
}

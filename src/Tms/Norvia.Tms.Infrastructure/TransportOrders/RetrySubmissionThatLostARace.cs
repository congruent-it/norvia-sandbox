using Ardalis.Result;
using Mediator;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Infrastructure.Data;
using Norvia.Tms.Infrastructure.Data.Configurations;

namespace Norvia.Tms.Infrastructure.TransportOrders;

/// <summary>
/// When the same booking arrives twice at once, both submissions find no Transport Order holding the Shipper reference,
/// and the unique index lets only one of them be accepted. The other is run once more: by then the winner is committed,
/// so it is answered as a repeat instead of failing.
/// </summary>
internal sealed class RetrySubmissionThatLostARace(TmsDbContext dbContext)
    : IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>
{
    private const int DuplicateKeyInUniqueIndex = 2601;

    public async ValueTask<Result<SubmitTransportOrderResult>> Handle(
        SubmitTransportOrderCommand message,
        MessageHandlerDelegate<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(message, cancellationToken);
        }
        catch (DbUpdateException exception) when (LostTheShipperReference(exception))
        {
            // The failed attempt's Transport Order and outbox messages are still tracked; none of them may be saved.
            dbContext.ChangeTracker.Clear();
            return await next(message, cancellationToken);
        }
    }

    private static bool LostTheShipperReference(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateKeyInUniqueIndex } sqlException
        && sqlException.Message.Contains(TransportOrderConfiguration.ShipperReferenceIndex, StringComparison.Ordinal);
}

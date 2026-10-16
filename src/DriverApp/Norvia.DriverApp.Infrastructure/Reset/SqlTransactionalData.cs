using Microsoft.EntityFrameworkCore;
using Norvia.DriverApp.Application.Reset;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Settings;
using Norvia.DriverApp.Domain.Trips;
using Norvia.DriverApp.Infrastructure.Data;
using Norvia.DriverApp.Infrastructure.Outbox;

namespace Norvia.DriverApp.Infrastructure.Reset;

/// <summary>
/// Deletes the Driver app's records in one transaction: the Drivers' reports before the Trips they belong to, the outbox, and the demo settings, which
/// fall back to their defaults. The Drivers themselves come from the seed file and are not in the database.
/// </summary>
internal sealed class SqlTransactionalData(DriverAppDbContext dbContext) : ITransactionalData
{
    public async Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var deleted = new List<DeletedKind>
            {
                await DeleteAsync<DriverReport>("Driver reports", cancellationToken),
                await DeleteAsync<DriverTrip>("Trips", cancellationToken),
                await DeleteAsync<OutboxMessage>("Messages", cancellationToken),
                await DeleteAsync<DriverAppSettings>("Demo settings", cancellationToken),
            };

            await transaction.CommitAsync(cancellationToken);
            return (IReadOnlyList<DeletedKind>)deleted;
        });
    }

    private async Task<DeletedKind> DeleteAsync<T>(string kind, CancellationToken cancellationToken)
        where T : class =>
        new(kind, await dbContext.Set<T>().ExecuteDeleteAsync(cancellationToken));
}

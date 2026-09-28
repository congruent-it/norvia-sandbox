using Microsoft.EntityFrameworkCore;

namespace Norvia.Tms.Infrastructure.Data;

/// <summary>Draws the next value of one of the TMS's number sequences (Transport Order, Shipment, Carrier booking).</summary>
internal static class SqlSequence
{
    public static async Task<long> NextAsync(TmsDbContext dbContext, string sequenceName, CancellationToken cancellationToken)
    {
        // Materialized as is: SQL Server doesn't allow NEXT VALUE FOR inside the subquery EF would compose around it.
        // sequenceName never comes from a caller outside Infrastructure: it is always one of TmsDbContext's own sequence
        // name constants, never data, so there is nothing here for EF1002 to actually catch.
#pragma warning disable EF1002
        var next = await dbContext.Database
            .SqlQueryRaw<long>($"SELECT NEXT VALUE FOR [{sequenceName}] AS [Value]")
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002
        return next.Single();
    }
}

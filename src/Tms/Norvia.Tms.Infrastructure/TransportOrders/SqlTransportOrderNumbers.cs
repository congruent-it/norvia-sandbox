using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.TransportOrders;

internal sealed class SqlTransportOrderNumbers(TmsDbContext dbContext) : ITransportOrderNumbers
{
    public async Task<TransportOrderNumber> NextAsync(CancellationToken cancellationToken)
    {
        // Materialized as is: SQL Server doesn't allow NEXT VALUE FOR inside the subquery EF would compose around it.
        var next = await dbContext.Database
            .SqlQueryRaw<long>($"SELECT NEXT VALUE FOR [{TmsDbContext.TransportOrderNumberSequence}] AS [Value]")
            .ToListAsync(cancellationToken);
        return TransportOrderNumber.FromSequence(next.Single());
    }
}

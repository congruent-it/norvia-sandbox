using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Shipments;

internal sealed class SqlShipmentNumbers(TmsDbContext dbContext) : IShipmentNumbers
{
    public async Task<ShipmentNumber> NextAsync(CancellationToken cancellationToken)
    {
        // Materialized as is: SQL Server doesn't allow NEXT VALUE FOR inside the subquery EF would compose around it.
        var next = await dbContext.Database
            .SqlQueryRaw<long>($"SELECT NEXT VALUE FOR [{TmsDbContext.ShipmentNumberSequence}] AS [Value]")
            .ToListAsync(cancellationToken);
        return ShipmentNumber.FromSequence(next.Single());
    }
}

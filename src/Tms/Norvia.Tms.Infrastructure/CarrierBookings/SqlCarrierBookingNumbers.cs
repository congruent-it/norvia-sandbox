using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.CarrierBookings;

internal sealed class SqlCarrierBookingNumbers(TmsDbContext dbContext) : ICarrierBookingNumbers
{
    public async Task<CarrierBookingNumber> NextAsync(CancellationToken cancellationToken)
    {
        // Materialized as is: SQL Server doesn't allow NEXT VALUE FOR inside the subquery EF would compose around it.
        var next = await dbContext.Database
            .SqlQueryRaw<long>($"SELECT NEXT VALUE FOR [{TmsDbContext.CarrierBookingNumberSequence}] AS [Value]")
            .ToListAsync(cancellationToken);
        return CarrierBookingNumber.FromSequence(next.Single());
    }
}

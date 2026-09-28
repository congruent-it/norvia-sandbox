using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.TransportOrders;

internal sealed class SqlTransportOrderNumbers(TmsDbContext dbContext) : ITransportOrderNumbers
{
    public async Task<TransportOrderNumber> NextAsync(CancellationToken cancellationToken) =>
        TransportOrderNumber.FromSequence(await SqlSequence.NextAsync(dbContext, TmsDbContext.TransportOrderNumberSequence, cancellationToken));
}

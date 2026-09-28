using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Shipments;

internal sealed class SqlShipmentNumbers(TmsDbContext dbContext) : IShipmentNumbers
{
    public async Task<ShipmentNumber> NextAsync(CancellationToken cancellationToken) =>
        ShipmentNumber.FromSequence(await SqlSequence.NextAsync(dbContext, TmsDbContext.ShipmentNumberSequence, cancellationToken));
}

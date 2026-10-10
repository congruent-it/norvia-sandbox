using Norvia.Tms.Application.Trips;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Trips;

internal sealed class SqlTripNumbers(TmsDbContext dbContext) : ITripNumbers
{
    public async Task<TripNumber> NextAsync(CancellationToken cancellationToken) =>
        TripNumber.FromSequence(await SqlSequence.NextAsync(dbContext, TmsDbContext.TripNumberSequence, cancellationToken));
}

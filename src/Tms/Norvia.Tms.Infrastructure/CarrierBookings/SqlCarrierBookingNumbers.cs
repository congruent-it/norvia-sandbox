using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.CarrierBookings;

internal sealed class SqlCarrierBookingNumbers(TmsDbContext dbContext) : ICarrierBookingNumbers
{
    public async Task<CarrierBookingNumber> NextAsync(CancellationToken cancellationToken) =>
        CarrierBookingNumber.FromSequence(await SqlSequence.NextAsync(dbContext, TmsDbContext.CarrierBookingNumberSequence, cancellationToken));
}

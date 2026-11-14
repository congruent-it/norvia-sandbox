using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Application.MasterData.RateCards.Read;

public sealed class GetRateCardHandler(IReadRepository<RateCard> rateCards) : IQueryHandler<GetRateCardQuery, RateCardRecord?>
{
    public async ValueTask<RateCardRecord?> Handle(GetRateCardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var card = await rateCards.FirstOrDefaultAsync(new RateCardByIdSpec(new RateCardId(query.Id)), cancellationToken);
        return card is null ? null : new RateCardRecord(card.Id.Value, card.ShipperId.Value, card.Revision, RateCardData.Of(card.Versions));
    }
}

public sealed class GetCarrierRateCardHandler(IReadRepository<CarrierRateCard> rateCards) : IQueryHandler<GetCarrierRateCardQuery, CarrierRateCardRecord?>
{
    public async ValueTask<CarrierRateCardRecord?> Handle(GetCarrierRateCardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var card = await rateCards.FirstOrDefaultAsync(new CarrierRateCardByIdSpec(new RateCardId(query.Id)), cancellationToken);
        return card is null ? null : new CarrierRateCardRecord(card.Id.Value, card.CarrierId.Value, card.Revision, RateCardData.Of(card.Versions));
    }
}

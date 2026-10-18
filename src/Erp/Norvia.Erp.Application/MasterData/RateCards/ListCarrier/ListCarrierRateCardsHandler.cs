using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Application.MasterData.RateCards.ListCarrier;

public sealed class ListCarrierRateCardsHandler(IReadRepository<CarrierRateCard> rateCards, IReadRepository<Carrier> carriers)
    : IQueryHandler<ListCarrierRateCardsQuery, IReadOnlyList<RateCardView>>
{
    public async ValueTask<IReadOnlyList<RateCardView>> Handle(ListCarrierRateCardsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cards = await rateCards.ListAsync(new CarrierRateCardsSpec(), cancellationToken);
        var names = (await carriers.ListAsync(new CarriersSpec(), cancellationToken)).ToDictionary(carrier => carrier.Id, carrier => carrier.Name);
        return [.. cards.OrderBy(card => card.Id.Value, StringComparer.Ordinal).Select(card => new RateCardView(card.Id.Value, card.CarrierId.Value, names.GetValueOrDefault(card.CarrierId, card.CarrierId.Value), RateCardViews.Of(card.Versions)))];
    }
}

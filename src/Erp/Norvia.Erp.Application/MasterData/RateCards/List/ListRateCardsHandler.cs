using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.MasterData.RateCards.List;

public sealed class ListRateCardsHandler(IReadRepository<RateCard> rateCards, IReadRepository<Shipper> shippers)
    : IQueryHandler<ListRateCardsQuery, IReadOnlyList<RateCardView>>
{
    public async ValueTask<IReadOnlyList<RateCardView>> Handle(ListRateCardsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cards = await rateCards.ListAsync(new RateCardsSpec(), cancellationToken);
        var names = (await shippers.ListAsync(new ShippersSpec(), cancellationToken)).ToDictionary(shipper => shipper.Id, shipper => shipper.Name);
        return [.. cards.OrderBy(card => card.Id.Value, StringComparer.Ordinal).Select(card => new RateCardView(card.Id.Value, card.ShipperId.Value, names.GetValueOrDefault(card.ShipperId, card.ShipperId.Value), RateCardViews.Of(card.Versions)))];
    }
}

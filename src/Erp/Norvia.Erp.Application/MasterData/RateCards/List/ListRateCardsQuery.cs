using Mediator;

namespace Norvia.Erp.Application.MasterData.RateCards.List;

/// <summary>The Shippers' Rate Cards in the ERP's master data, by ID.</summary>
public sealed record ListRateCardsQuery : IQuery<IReadOnlyList<RateCardView>>;

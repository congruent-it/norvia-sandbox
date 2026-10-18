using Mediator;

namespace Norvia.Erp.Application.MasterData.RateCards.ListCarrier;

/// <summary>The Carriers' Carrier Rate Cards in the ERP's master data, by ID.</summary>
public sealed record ListCarrierRateCardsQuery : IQuery<IReadOnlyList<RateCardView>>;

using Mediator;

namespace Norvia.Erp.Application.MasterData.RateCards.Read;

/// <summary>A Shipper's Rate Card whole, with every version and everything it prices, for Finance to edit. Null when there is none.</summary>
public sealed record GetRateCardQuery(string Id) : IQuery<RateCardRecord?>;

/// <summary>A Carrier's Carrier Rate Card whole, for Finance to edit. Null when there is none.</summary>
public sealed record GetCarrierRateCardQuery(string Id) : IQuery<CarrierRateCardRecord?>;

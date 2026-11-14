using Norvia.Erp.Application.MasterData.RateCards;

namespace Norvia.Erp.Api.MasterData;

/// <summary>The body of <c>PUT /v1/carrier-rate-cards/{id}</c>: the whole Carrier Rate Card but for its ID, which the route carries. The versions replace the card's whole list.</summary>
public sealed record SaveCarrierRateCardRequest(string CarrierId, IReadOnlyList<RateCardVersionData> Versions);

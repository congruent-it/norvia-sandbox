using Norvia.Tms.Application.RateCards;

namespace Norvia.Tms.Api.RateCards;

/// <summary>The body of <c>PUT /v1/rate-cards/{id}</c>: the ERP's Rate Card whole, with its list of dated versions and its Master-data revision.</summary>
public sealed record ReplaceRateCardRequest(string ShipperId, int Revision, IReadOnlyList<RateCardVersionData> Versions);

/// <summary>The body of <c>PUT /v1/carrier-rate-cards/{id}</c>: the ERP's Carrier Rate Card whole, with its list of dated versions and its Master-data revision.</summary>
public sealed record ReplaceCarrierRateCardRequest(string CarrierId, int Revision, IReadOnlyList<RateCardVersionData> Versions);

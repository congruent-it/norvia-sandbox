namespace Norvia.Tms.Api.Shipments;

/// <summary>The Spot price the Planner releases a Carrier Shipment at (P9).</summary>
/// <param name="Amount">Required: in EUR, without VAT, above zero and in whole cents.</param>
/// <param name="Remark">Required: where the price comes from, for example "agreed with Van Dijk by phone, €480".</param>
public sealed record ReleaseWithSpotPriceRequest(decimal? Amount, string? Remark);

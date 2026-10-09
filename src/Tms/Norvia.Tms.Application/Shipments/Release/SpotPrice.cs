namespace Norvia.Tms.Application.Shipments.Release;

/// <summary>A price the Planner agreed with a Carrier outside its Carrier Rate Card (P9).</summary>
/// <param name="Amount">In EUR, without VAT, in whole cents.</param>
/// <param name="Remark">Where the price comes from, for example "agreed with Van Dijk by phone, €480".</param>
public sealed record SpotPrice(decimal Amount, string Remark);

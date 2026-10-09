using System.Text.Json.Serialization;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.CarrierBookings;

/// <summary>A Carrier cost as the query API and the Canonical Model show it (contracts/canonical-model/carrier-cost.v1.schema.json).</summary>
/// <param name="Currency">Always EUR, without VAT.</param>
/// <param name="Total">The sum of the charge lines.</param>
/// <param name="CalculatedAt">When the Carrier cost was calculated, at release.</param>
public sealed record CarrierCostView(
    string Currency,
    decimal Total,
    IReadOnlyList<ChargeLineView> ChargeLines,
    CarrierCostSourceView Source,
    DateTimeOffset CalculatedAt)
{
    public static CarrierCostView? From(CarrierCost? carrierCost) =>
        carrierCost is null
            ? null
            : new(
                Money.Currency,
                carrierCost.Total.Amount,
                [.. carrierCost.ChargeLines.Select(line => new ChargeLineView(line.Type.ToString(), line.Amount.Amount))],
                CarrierCostSourceView.From(carrierCost.Source),
                carrierCost.CalculatedAt);

    public bool Equals(CarrierCostView? other) =>
        other is not null
        && Currency == other.Currency
        && Total == other.Total
        && ChargeLines.SequenceEqual(other.ChargeLines)
        && Source == other.Source
        && CalculatedAt == other.CalculatedAt;

    public override int GetHashCode() =>
        ChargeLines.Aggregate(HashCode.Combine(Currency, Total, Source, CalculatedAt), (hash, line) => HashCode.Combine(hash, line));
}

/// <param name="Kind">Where the Carrier cost came from: <c>CarrierRateCard</c> or <c>Spot</c>.</param>
/// <param name="RateCardVersion">The version of the Carrier Rate Card that priced it; none for a Spot price.</param>
/// <param name="Remark">The Planner's remark with a Spot price; none for a Carrier Rate Card.</param>
public sealed record CarrierCostSourceView(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RateCardVersionView? RateCardVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Remark = null)
{
    public static CarrierCostSourceView From(CarrierCostSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.Kind.ToString(),
            source.RateCardVersion is { } version ? new RateCardVersionView(version.RateCardId.Value, version.Version) : null,
            source.Remark);
    }
}

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

    public override int GetHashCode() => HashCode.Combine(Currency, Total, ChargeLines.Count, Source, CalculatedAt);
}

/// <param name="Kind">Where the Carrier cost came from, such as <c>CarrierRateCard</c>.</param>
/// <param name="RateCardVersion">The version of the Carrier Rate Card that priced it.</param>
public sealed record CarrierCostSourceView(string Kind, RateCardVersionView? RateCardVersion)
{
    public static CarrierCostSourceView From(CarrierCostSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.Kind.ToString(),
            source.RateCardVersion is { } version ? new RateCardVersionView(version.RateCardId.Value, version.Version) : null);
    }
}

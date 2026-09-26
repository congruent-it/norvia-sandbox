using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <summary>A Freight Charge as the query API and the Canonical Model show it.</summary>
/// <param name="Currency">Always EUR, without VAT.</param>
/// <param name="Total">The sum of the charge lines.</param>
public sealed record FreightChargeView(string Currency, decimal Total, IReadOnlyList<ChargeLineView> ChargeLines, RateCardVersionView RateCardVersion)
{
    public static FreightChargeView? From(FreightCharge? freightCharge) =>
        freightCharge is null
            ? null
            : new(
                Money.Currency,
                freightCharge.Total.Amount,
                [.. freightCharge.ChargeLines.Select(line => new ChargeLineView(line.Type.ToString(), line.Amount.Amount))],
                new RateCardVersionView(freightCharge.RateCardVersion.RateCardId.Value, freightCharge.RateCardVersion.Version));

    public bool Equals(FreightChargeView? other) =>
        other is not null
        && Currency == other.Currency
        && Total == other.Total
        && ChargeLines.SequenceEqual(other.ChargeLines)
        && RateCardVersion == other.RateCardVersion;

    public override int GetHashCode() => HashCode.Combine(Currency, Total, ChargeLines.Count, RateCardVersion);
}

/// <param name="Type">What the line charges for, such as <c>Freight</c>.</param>
/// <param name="Amount">In EUR without VAT, rounded to cents.</param>
public sealed record ChargeLineView(string Type, decimal Amount);

/// <summary>The Rate Card version a Freight Charge was calculated from.</summary>
public sealed record RateCardVersionView(string RateCardId, int Version);

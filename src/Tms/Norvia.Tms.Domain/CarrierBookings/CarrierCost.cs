using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// What Norvia pays a Carrier for one Carrier booking (P9): its charge lines, the source it was calculated from and
/// when. Frozen on the Carrier booking at release, it never changes. It is never the Shipper's Freight Charge.
/// </summary>
public sealed record CarrierCost(IReadOnlyList<ChargeLine> ChargeLines, CarrierCostSource Source, DateTimeOffset CalculatedAt)
{
    /// <summary>The sum of the charge lines, each already rounded to cents.</summary>
    public Money Total => Money.Sum(ChargeLines.Select(line => line.Amount));

    /// <summary>The Carrier cost a Carrier Rate Card's pricing makes, calculated at the given time.</summary>
    public static CarrierCost From(FreightCharge priced, DateTimeOffset calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(priced);
        return new(priced.ChargeLines, CarrierCostSource.CarrierRateCard(priced.RateCardVersion), calculatedAt);
    }

    /// <summary>
    /// The Carrier cost a Planner enters as a Spot price: one Freight line of a positive amount in cents, with the remark
    /// that says where it comes from.
    /// </summary>
    public static CarrierCost Spot(Money amount, string remark, DateTimeOffset calculatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(amount.Amount, 0m);
        if (amount.RoundedToCents() != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A Spot price is an amount in whole cents.");
        }

        return new([new ChargeLine(ChargeLineType.Freight, amount)], CarrierCostSource.Spot(remark), calculatedAt);
    }

    public bool Equals(CarrierCost? other) =>
        other is not null && Source == other.Source && CalculatedAt == other.CalculatedAt && ChargeLines.SequenceEqual(other.ChargeLines);

    public override int GetHashCode() =>
        ChargeLines.Aggregate(HashCode.Combine(Source, CalculatedAt), (hash, line) => HashCode.Combine(hash, line));
}

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>A Carrier cost is identified by the TMS's Carrier booking number, such as <c>CB-0000123</c>: there is one per Carrier booking.</summary>
public readonly record struct CarrierCostId : IComparable<CarrierCostId>, IComparable
{
    public CarrierCostId(string carrierBookingNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierBookingNumber);
        CarrierBookingNumber = carrierBookingNumber;
    }

    public string CarrierBookingNumber { get; }

    public static bool operator <(CarrierCostId left, CarrierCostId right) => left.CompareTo(right) < 0;

    public static bool operator <=(CarrierCostId left, CarrierCostId right) => left.CompareTo(right) <= 0;

    public static bool operator >(CarrierCostId left, CarrierCostId right) => left.CompareTo(right) > 0;

    public static bool operator >=(CarrierCostId left, CarrierCostId right) => left.CompareTo(right) >= 0;

    public int CompareTo(CarrierCostId other) => string.CompareOrdinal(CarrierBookingNumber, other.CarrierBookingNumber);

    public int CompareTo(object? obj) => obj is CarrierCostId other ? CompareTo(other) : 1;

    public override string ToString() => CarrierBookingNumber;
}

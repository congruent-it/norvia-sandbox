using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

/// <summary>
/// The Carrier cost is what Norvia pays a Carrier for one Carrier booking (P9): the charge lines the Carrier Rate Card
/// priced, their total, the source it came from and when it was calculated.
/// </summary>
public sealed class CarrierCostTests
{
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 24, 12, 45, 0, TimeSpan.Zero);
    private static readonly RateCardVersionReference Version = new(new RateCardId("CRC-0001"), 3);

    [Fact]
    public void From_APricedCarrierRateCard_KeepsItsChargeLines_AndNamesTheCarrierRateCardVersionAsItsSource()
    {
        var priced = new FreightCharge(
            [new ChargeLine(ChargeLineType.Freight, new Money(11.00m)), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, new Money(24.00m))],
            Version);

        var cost = CarrierCost.From(priced, CalculatedAt);

        cost.ChargeLines.Should().Equal(priced.ChargeLines);
        cost.Source.Should().Be(CarrierCostSource.CarrierRateCard(Version));
        cost.CalculatedAt.Should().Be(CalculatedAt);
    }

    [Fact]
    public void Total_IsTheSumOfTheChargeLines()
    {
        var cost = new CarrierCost(
            [new ChargeLine(ChargeLineType.Freight, new Money(11.00m)), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, new Money(24.00m))],
            CarrierCostSource.CarrierRateCard(Version),
            CalculatedAt);

        cost.Total.Should().Be(new Money(35.00m));
    }

    [Fact]
    public void Equals_TheSameLinesSourceAndTime_AreEqual()
    {
        CarrierCost Cost() => new([new ChargeLine(ChargeLineType.Freight, new Money(11.00m))], CarrierCostSource.CarrierRateCard(Version), CalculatedAt);

        Cost().Should().Be(Cost());
        Cost().GetHashCode().Should().Be(Cost().GetHashCode());
    }
}

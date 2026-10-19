using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Domain.UnitTests.CarrierCosts;

public sealed class CarrierCostTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly CarrierCostData Data = new(
        "CB-0000123",
        "TRX-998",
        new CarrierId("CAR-0001"),
        "SH-0000123",
        "TO-0000123",
        new CarrierCostCharge(70.50m, [new ChargeLine(ChargeLineType.Freight, 60.50m), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, 10m)]),
        CarrierCostSource.FromRateCard("CRC-0001", 2),
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
        "delivered-1");

    [Fact]
    public void Record_WithAConsistentCharge_IsOpenWithEverythingTheEventCarries()
    {
        var (cost, refusal) = CarrierCost.Record(Data, Now);

        refusal.Should().BeNull();
        cost!.Id.CarrierBookingNumber.Should().Be("CB-0000123");
        cost.CarrierBookingReference.Should().Be("TRX-998");
        cost.CarrierId.Value.Should().Be("CAR-0001");
        cost.ShipmentNumber.Should().Be("SH-0000123");
        cost.TransportOrderNumber.Should().Be("TO-0000123");
        cost.Total.Should().Be(70.50m);
        cost.Source.Kind.Should().Be(CarrierCostSourceKind.CarrierRateCard);
        cost.DeliveryTime.Offset.Should().Be(TimeSpan.FromHours(2));
        cost.CorrelationId.Should().Be("delivered-1");
        cost.RecordedAt.Should().Be(Now);
        cost.Status.Should().Be(CarrierCostStatus.Open);
    }

    [Fact]
    public void Record_WithoutChargeLines_IsRefused()
    {
        var (cost, refusal) = CarrierCost.Record(Data with { Charge = new CarrierCostCharge(0, []) }, Now);

        cost.Should().BeNull();
        refusal!.Code.Should().Be(CarrierCostRefusalCode.CarrierCostWithoutChargeLines);
    }

    [Fact]
    public void Record_WithATotalThatIsNotTheSumOfTheLines_IsRefused()
    {
        var (cost, refusal) = CarrierCost.Record(Data with { Charge = Data.Charge with { Total = 99m } }, Now);

        cost.Should().BeNull();
        refusal!.Code.Should().Be(CarrierCostRefusalCode.CarrierCostTotalMismatch);
        refusal.Message.Should().Contain("CB-0000123").And.Contain("99").And.Contain("70.50");
    }

    [Fact]
    public void HasSameContentAs_TheSameContentWithAnotherCorrelationId_IsTrue()
    {
        var cost = CarrierCost.Record(Data, Now).Cost!;

        cost.HasSameContentAs(Data with { CorrelationId = "delivered-2" }).Should().BeTrue();
    }

    [Fact]
    public void HasSameContentAs_TheSameInstantAtAnotherOffset_IsFalse()
    {
        var cost = CarrierCost.Record(Data, Now).Cost!;

        cost.HasSameContentAs(Data with { DeliveryTime = Data.DeliveryTime.ToOffset(TimeSpan.Zero) }).Should().BeFalse();
    }

    public static TheoryData<CarrierCostData> Changed => new()
    {
        Data with { CarrierBookingNumber = "CB-0000124" },
        Data with { CarrierBookingReference = "TRX-999" },
        Data with { CarrierId = new CarrierId("CAR-0002") },
        Data with { ShipmentNumber = "SH-0000124" },
        Data with { TransportOrderNumber = "TO-0000124" },
        Data with { DeliveryTime = Data.DeliveryTime.AddMinutes(1) },
        Data with { Charge = new CarrierCostCharge(70.50m, [new ChargeLine(ChargeLineType.Freight, 70.50m)]) },
        Data with { Charge = Data.Charge with { Total = 71m } },
        Data with { Source = CarrierCostSource.FromRateCard("CRC-0001", 3) },
        Data with { Source = CarrierCostSource.FromSpot("Ferry strike") },
    };

    [Theory]
    [MemberData(nameof(Changed))]
    public void HasSameContentAs_ChangedContent_IsFalse(CarrierCostData changed)
    {
        CarrierCost.Record(Data, Now).Cost!.HasSameContentAs(changed).Should().BeFalse();
    }

    [Fact]
    public void Source_ASpotPrice_HasTheRemarkAndNoVersion()
    {
        var source = CarrierCostSource.FromSpot("Ferry strike");

        source.IsSpot.Should().BeTrue();
        (source.Remark, source.RateCardId, source.RateCardVersion).Should().Be(("Ferry strike", null, null));
    }

    [Fact]
    public void Source_ARateCard_HasTheVersionAndNoRemark()
    {
        var source = CarrierCostSource.FromRateCard("CRC-0001", 2);

        source.IsSpot.Should().BeFalse();
        (source.Remark, source.RateCardId, source.RateCardVersion).Should().Be((null, "CRC-0001", 2));
    }

    [Fact]
    public void Charge_EqualChargesHaveEqualHashCodes()
    {
        var other = new CarrierCostCharge(Data.Charge.Total, [.. Data.Charge.ChargeLines]);

        other.Equals(Data.Charge).Should().BeTrue();
        other.GetHashCode().Should().Be(Data.Charge.GetHashCode());
    }

    [Fact]
    public void Id_ComparesByBookingNumber()
    {
        var low = new CarrierCostId("CB-0000001");
        var high = new CarrierCostId("CB-0000002");

        (low < high).Should().BeTrue();
        (low <= high).Should().BeTrue();
        (high > low).Should().BeTrue();
        (high >= low).Should().BeTrue();
        low.CompareTo((object)high).Should().BeNegative();
        low.CompareTo(null).Should().Be(1);
        low.ToString().Should().Be("CB-0000001");
    }
}

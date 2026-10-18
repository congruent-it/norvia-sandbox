using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.BillableLines;

public sealed class BillableLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly BillableLineData Data = new(
        "TO-0000123",
        new ShipperId("SHP-0001"),
        "PO-77",
        "SH-0000123",
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
        new FreightCharge(
            100.50m,
            [new ChargeLine(ChargeLineType.Freight, 80.50m), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, 20m)],
            "RC-0001",
            2),
        ReleasedWithoutProofOfDelivery: false,
        "intake-1",
        "billing-1");

    [Fact]
    public void Record_WithAConsistentFreightCharge_IsWaitingWithEverythingTheEventCarries()
    {
        var (line, refusal) = BillableLine.Record(Data, Now);

        refusal.Should().BeNull();
        line.Should().NotBeNull();
        line!.Id.Should().Be(new BillableLineId("TO-0000123"));
        line.Status.Should().Be(BillableLineStatus.Waiting);
        line.ShipperId.Should().Be(Data.ShipperId);
        line.ShipperReference.Should().Be("PO-77");
        line.ShipmentNumber.Should().Be("SH-0000123");
        line.DeliveryTime.Should().Be(Data.DeliveryTime);
        line.FreightCharge.Should().Be(Data.FreightCharge);
        line.Total.Should().Be(100.50m);
        line.ReleasedWithoutProofOfDelivery.Should().BeFalse();
        line.IntakeCorrelationId.Should().Be("intake-1");
        line.BillingCorrelationId.Should().Be("billing-1");
        line.RecordedAt.Should().Be(Now);
    }

    [Fact]
    public void Record_WithoutChargeLines_IsRefusedWithItsCode()
    {
        var data = Data with { FreightCharge = Data.FreightCharge with { Total = 0m, ChargeLines = [] } };

        var (line, refusal) = BillableLine.Record(data, Now);

        line.Should().BeNull();
        refusal!.Code.Should().Be(BillableLineRefusalCode.FreightChargeWithoutChargeLines);
        refusal.Message.Should().Contain("TO-0000123").And.Contain("no charge lines");
    }

    [Fact]
    public void Record_WhoseTotalIsNotTheSumOfItsLines_IsRefusedWithItsCode()
    {
        var data = Data with { FreightCharge = Data.FreightCharge with { Total = 100m } };

        var (line, refusal) = BillableLine.Record(data, Now);

        line.Should().BeNull();
        refusal!.Code.Should().Be(BillableLineRefusalCode.FreightChargeTotalMismatch);
        refusal.Message.Should().Contain("100").And.Contain("100.50");
    }

    [Fact]
    public void HasSameContentAs_TheSameEvent_IsTrue()
    {
        var line = BillableLine.Record(Data, Now).Line!;

        line.HasSameContentAs(Data with { ShipperReference = "PO-77" }).Should().BeTrue();
    }

    [Fact]
    public void HasSameContentAs_ARetryOnAnotherRun_IsTrue()
    {
        var line = BillableLine.Record(Data, Now).Line!;

        line.HasSameContentAs(Data with { BillingCorrelationId = "billing-2", IntakeCorrelationId = "intake-2" }).Should().BeTrue();
    }

    [Fact]
    public void HasSameContentAs_TheSameInstantAtTheSameOffset_IsTrue()
    {
        var line = BillableLine.Record(Data, Now).Line!;

        line.HasSameContentAs(Data with { DeliveryTime = new DateTimeOffset(2026, 10, 17, 12, 30, 0, TimeSpan.FromHours(0)) }).Should().BeFalse("the offset of where it happened is content too");
    }

    public static TheoryData<string, Func<BillableLineData, BillableLineData>> Changes => new()
    {
        { "Shipper", data => data with { ShipperId = new ShipperId("SHP-0002") } },
        { "Shipper reference", data => data with { ShipperReference = "PO-78" } },
        { "Shipment number", data => data with { ShipmentNumber = "SH-0000124" } },
        { "Delivery time", data => data with { DeliveryTime = data.DeliveryTime.AddMinutes(1) } },
        { "Release without Proof of delivery", data => data with { ReleasedWithoutProofOfDelivery = true } },
        { "Rate Card version", data => data with { FreightCharge = data.FreightCharge with { RateCardVersion = 3 } } },
        { "Rate Card", data => data with { FreightCharge = data.FreightCharge with { RateCardId = "RC-0002" } } },
        { "Charge line amount", data => data with { FreightCharge = data.FreightCharge with { ChargeLines = [new ChargeLine(ChargeLineType.Freight, 100.50m)] } } },
        { "Transport Order", data => data with { TransportOrderNumber = "TO-0000124" } },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void HasSameContentAs_ADifferentContent_IsFalse(string what, Func<BillableLineData, BillableLineData> change)
    {
        var line = BillableLine.Record(Data, Now).Line!;

        line.HasSameContentAs(change(Data)).Should().BeFalse(what);
    }

    [Fact]
    public void Conflict_NamesTheTransportOrderAndHasItsCode()
    {
        var refusal = BillableLineRefusal.Conflict("TO-0000123");

        refusal.Code.Should().Be(BillableLineRefusalCode.BillableLineConflict);
        refusal.Message.Should().Contain("TO-0000123").And.Contain("different content");
    }

    [Fact]
    public void ShipperNotFound_NamesTheShipperAndHasItsCode()
    {
        var refusal = BillableLineRefusal.ShipperNotFound("SHP-9999");

        refusal.Code.Should().Be(BillableLineRefusalCode.ShipperNotFound);
        refusal.Message.Should().Contain("SHP-9999");
    }

    [Fact]
    public void Specs_FindByNumber_AndListOnlyWaitingLinesOfTheShipper_EarliestDeliveryFirst()
    {
        var later = BillableLine.Record(Data with { TransportOrderNumber = "TO-0000124", DeliveryTime = Data.DeliveryTime.AddDays(1) }, Now).Line!;
        var earlier = BillableLine.Record(Data, Now).Line!;
        var other = BillableLine.Record(Data with { TransportOrderNumber = "TO-0000125", ShipperId = new ShipperId("SHP-0002") }, Now).Line!;
        List<BillableLine> all = [later, earlier, other];

        new BillableLineByTransportOrderNumberSpec("TO-0000124").Evaluate(all).Should().Equal(later);
        new WaitingBillableLinesSpec().Evaluate(all).Select(line => line.Id.TransportOrderNumber).Should().Equal("TO-0000123", "TO-0000125", "TO-0000124");
        new WaitingBillableLinesOfShipperSpec(new ShipperId("SHP-0001")).Evaluate(all).Select(line => line.Id.TransportOrderNumber).Should().Equal("TO-0000123", "TO-0000124");
    }

    [Fact]
    public void BillableLineId_WithoutANumber_Throws()
    {
        var creating = () => new BillableLineId(" ");

        creating.Should().Throw<ArgumentException>();
    }
}

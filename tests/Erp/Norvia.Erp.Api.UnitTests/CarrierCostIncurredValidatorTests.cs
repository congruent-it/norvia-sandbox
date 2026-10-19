using Norvia.Erp.Api.CarrierCosts;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Api.UnitTests;

public sealed class CarrierCostIncurredValidatorTests
{
    internal static readonly CarrierCostIncurredRequest Valid = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "CarrierCostIncurred",
        1,
        new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero),
        "delivered-1",
        "CB-0000123",
        "TRX-998",
        "SH-0000123",
        "TO-0000123",
        "CAR-0001",
        new CarrierCostRequest(
            "EUR",
            70.50m,
            [new CarrierCostChargeLineRequest(ChargeLineType.Freight, 60.50m), new CarrierCostChargeLineRequest(ChargeLineType.MinimumChargeAdjustment, 10m)],
            new CarrierCostSourceRequest(CarrierCostSourceKind.CarrierRateCard, new CarrierRateCardVersionRequest("CRC-0001", 2)),
            new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero)),
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)));

    private static CarrierCostIncurredRequest With(Func<CarrierCostRequest, CarrierCostRequest> cost) => Valid with { CarrierCost = cost(Valid.CarrierCost) };

    [Fact]
    public void Validate_ACanonicalEvent_MapsEverythingItCarries()
    {
        var (cost, errors) = CarrierCostIncurredValidator.Validate(Valid);

        errors.Should().BeEmpty();
        cost!.CarrierBookingNumber.Should().Be("CB-0000123");
        cost.CarrierBookingReference.Should().Be("TRX-998");
        cost.CarrierId.Value.Should().Be("CAR-0001");
        cost.ShipmentNumber.Should().Be("SH-0000123");
        cost.TransportOrderNumber.Should().Be("TO-0000123");
        cost.DeliveryTime.Offset.Should().Be(TimeSpan.FromHours(2));
        cost.CorrelationId.Should().Be("delivered-1");
        cost.Charge.Total.Should().Be(70.50m);
        cost.Charge.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, 60.50m), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, 10m));
        cost.Source.Should().Be(CarrierCostSource.FromRateCard("CRC-0001", 2));
    }

    [Fact]
    public void Validate_ASpotPrice_MapsTheRemark()
    {
        var spot = With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.Spot, Remark: "Ferry strike") });

        var (cost, errors) = CarrierCostIncurredValidator.Validate(spot);

        errors.Should().BeEmpty();
        cost!.Source.Should().Be(CarrierCostSource.FromSpot("Ferry strike"));
    }

    [Fact]
    public void Validate_NoBody_IsRefused()
    {
        var (cost, errors) = CarrierCostIncurredValidator.Validate(null);

        cost.Should().BeNull();
        errors.Should().ContainKey(string.Empty);
    }

    [Fact]
    public void Validate_ChargeLinesThatDontAddUp_ArePassedOnForTheDomainToRefuse()
    {
        var (cost, errors) = CarrierCostIncurredValidator.Validate(With(c => c with { Total = 99m }));

        errors.Should().BeEmpty();
        cost.Should().NotBeNull();
    }

    public static TheoryData<string, CarrierCostIncurredRequest> Invalid => new()
    {
        { "EventType", Valid with { EventType = "TransportOrderBillable" } },
        { "EventVersion", Valid with { EventVersion = 2 } },
        { "CorrelationId", Valid with { CorrelationId = " " } },
        { "CarrierBookingNumber", Valid with { CarrierBookingNumber = "CB-12" } },
        { "CarrierBookingReference", Valid with { CarrierBookingReference = "" } },
        { "ShipmentNumber", Valid with { ShipmentNumber = "SH-1" } },
        { "TransportOrderNumber", Valid with { TransportOrderNumber = "TO-1" } },
        { "CarrierId", Valid with { CarrierId = "" } },
        { "CarrierCost.Currency", With(c => c with { Currency = "USD" }) },
        { "CarrierCost.Total", With(c => c with { Total = -1m }) },
        { "CarrierCost.Total", With(c => c with { Total = 70.505m }) },
        { "CarrierCost.ChargeLines", With(c => c with { ChargeLines = [new CarrierCostChargeLineRequest(ChargeLineType.Freight, -1m)] }) },
        { "CarrierCost.ChargeLines", With(c => c with { ChargeLines = [new CarrierCostChargeLineRequest(ChargeLineType.Freight, 1.005m)] }) },
        { "CarrierCost.Source", With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.CarrierRateCard) }) },
        { "CarrierCost.Source", With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.CarrierRateCard, new CarrierRateCardVersionRequest("CRC-0001", 0)) }) },
        { "CarrierCost.Source", With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.CarrierRateCard, new CarrierRateCardVersionRequest(" ", 1)) }) },
        { "CarrierCost.Source", With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.CarrierRateCard, new CarrierRateCardVersionRequest("CRC-0001", 1), "remark") }) },
        { "CarrierCost.Source", With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.Spot) }) },
        { "CarrierCost.Source", With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.Spot, Remark: new string('x', 501)) }) },
        { "CarrierCost.Source", With(c => c with { Source = new CarrierCostSourceRequest(CarrierCostSourceKind.Spot, new CarrierRateCardVersionRequest("CRC-0001", 1), "remark") }) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validate_BreakingTheCanonicalModel_IsRefusedOnTheField(string field, CarrierCostIncurredRequest request)
    {
        var (cost, errors) = CarrierCostIncurredValidator.Validate(request);

        cost.Should().BeNull();
        errors.Should().ContainKey(field);
    }
}

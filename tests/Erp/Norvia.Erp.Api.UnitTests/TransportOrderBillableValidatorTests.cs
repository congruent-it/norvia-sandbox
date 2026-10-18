using Norvia.Erp.Api.BillableLines;
using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Api.UnitTests;

public sealed class TransportOrderBillableValidatorTests
{
    private static readonly TransportOrderBillableRequest Valid = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "TransportOrderBillable",
        1,
        new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero),
        "billing-1",
        "intake-1",
        "TO-0000123",
        "SHP-0001",
        "PO-77",
        "SH-0000123",
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
        new FreightChargeRequest(
            "EUR",
            100.50m,
            [new ChargeLineRequest(ChargeLineType.Freight, 80.50m), new ChargeLineRequest(ChargeLineType.MinimumChargeAdjustment, 20m)],
            new RateCardVersionRequest("RC-0001", 2)),
        ReleasedWithoutProofOfDelivery: true);

    [Fact]
    public void Validate_ACanonicalEvent_MapsEverythingItCarries()
    {
        var (line, errors) = TransportOrderBillableValidator.Validate(Valid);

        errors.Should().BeEmpty();
        line.Should().NotBeNull();
        line!.TransportOrderNumber.Should().Be("TO-0000123");
        line.ShipperId.Value.Should().Be("SHP-0001");
        line.ShipperReference.Should().Be("PO-77");
        line.ShipmentNumber.Should().Be("SH-0000123");
        line.DeliveryTime.Should().Be(Valid.DeliveryTime);
        line.DeliveryTime.Offset.Should().Be(TimeSpan.FromHours(2));
        line.ReleasedWithoutProofOfDelivery.Should().BeTrue();
        line.IntakeCorrelationId.Should().Be("intake-1");
        line.BillingCorrelationId.Should().Be("billing-1");
        line.FreightCharge.Total.Should().Be(100.50m);
        line.FreightCharge.RateCardId.Should().Be("RC-0001");
        line.FreightCharge.RateCardVersion.Should().Be(2);
        line.FreightCharge.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, 80.50m), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, 20m));
    }

    [Fact]
    public void Validate_NoBody_IsRefused()
    {
        var (line, errors) = TransportOrderBillableValidator.Validate(null);

        line.Should().BeNull();
        errors.Should().ContainKey(string.Empty);
    }

    [Fact]
    public void Validate_ChargeLinesThatDontAddUp_ArePassedOnForTheDomainToRefuse()
    {
        var request = Valid with { FreightCharge = Valid.FreightCharge with { Total = 1m } };

        var (line, errors) = TransportOrderBillableValidator.Validate(request);

        errors.Should().BeEmpty("the billing rule has its own stable code");
        line!.FreightCharge.Total.Should().Be(1m);
    }

    [Fact]
    public void Validate_NoChargeLines_ArePassedOnForTheDomainToRefuse()
    {
        var request = Valid with { FreightCharge = Valid.FreightCharge with { Total = 0m, ChargeLines = [] } };

        var (line, errors) = TransportOrderBillableValidator.Validate(request);

        errors.Should().BeEmpty();
        line!.FreightCharge.ChargeLines.Should().BeEmpty();
    }

    public static TheoryData<string, Func<TransportOrderBillableRequest, TransportOrderBillableRequest>, string> Invalid => new()
    {
        { "event type", request => request with { EventType = "TransportOrderRated" }, "EventType" },
        { "event version", request => request with { EventVersion = 2 }, "EventVersion" },
        { "correlation ID", request => request with { CorrelationId = " " }, "CorrelationId" },
        { "intake correlation ID", request => request with { IntakeCorrelationId = "" }, "IntakeCorrelationId" },
        { "Transport Order number", request => request with { TransportOrderNumber = "TO-123" }, "TransportOrderNumber" },
        { "Shipper", request => request with { ShipperId = "" }, "ShipperId" },
        { "Shipper reference", request => request with { ShipperReference = " " }, "ShipperReference" },
        { "Shipment number", request => request with { ShipmentNumber = "SHIP-1" }, "ShipmentNumber" },
        { "currency", request => request with { FreightCharge = request.FreightCharge with { Currency = "USD" } }, "FreightCharge.Currency" },
        { "negative total", request => request with { FreightCharge = request.FreightCharge with { Total = -1m } }, "FreightCharge.Total" },
        { "total in fractions of a cent", request => request with { FreightCharge = request.FreightCharge with { Total = 100.505m } }, "FreightCharge.Total" },
        {
            "charge line in fractions of a cent",
            request => request with { FreightCharge = request.FreightCharge with { ChargeLines = [new ChargeLineRequest(ChargeLineType.Freight, 1.005m)] } },
            "FreightCharge.ChargeLines"
        },
        { "Rate Card", request => request with { FreightCharge = request.FreightCharge with { RateCardVersion = new RateCardVersionRequest("", 1) } }, "FreightCharge.RateCardVersion" },
        { "Rate Card version", request => request with { FreightCharge = request.FreightCharge with { RateCardVersion = new RateCardVersionRequest("RC-0001", 0) } }, "FreightCharge.RateCardVersion" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validate_BreakingTheCanonicalModel_IsAClientError(string what, Func<TransportOrderBillableRequest, TransportOrderBillableRequest> change, string field)
    {
        var (line, errors) = TransportOrderBillableValidator.Validate(change(Valid));

        line.Should().BeNull(what);
        errors.Should().ContainKey(field);
    }
}

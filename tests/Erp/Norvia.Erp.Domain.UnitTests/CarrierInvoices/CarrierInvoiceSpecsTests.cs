using Ardalis.Specification;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceSpecsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static CarrierInvoice Invoice(string carrierId, string number, DateTimeOffset at) =>
        CarrierInvoice.Receive(
            new CarrierInvoiceData(new CarrierId(carrierId), number, new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-1", 5m, null)], "invoice"),
            [],
            0m,
            at).Invoice!;

    private static CarrierCost Cost(string number, string reference, string carrierId) =>
        CarrierCost.Record(
            new CarrierCostData(
                number, reference, new CarrierId(carrierId), "SH-0000001", "TO-0000001",
                new CarrierCostCharge(5m, [new ChargeLine(ChargeLineType.Freight, 5m)]), CarrierCostSource.FromSpot("remark"), Now, "delivered"),
            Now).Cost!;

    [Fact]
    public void ByNumber_FindsOnlyThatCarriersInvoiceWithThatNumber()
    {
        var wanted = Invoice("CAR-0001", "A-1", Now);
        CarrierInvoice[] all = [wanted, Invoice("CAR-0002", "A-1", Now), Invoice("CAR-0001", "A-2", Now)];

        new CarrierInvoiceByNumberSpec(new CarrierId("CAR-0001"), "A-1").Evaluate(all).Should().Equal(wanted);
    }

    [Fact]
    public void NewestFirst_OrdersByReceivedTimeDescending()
    {
        var old = Invoice("CAR-0001", "A-1", Now);
        var recent = Invoice("CAR-0001", "A-2", Now.AddHours(1));

        new CarrierInvoicesNewestFirstSpec().Evaluate([old, recent]).Should().Equal(recent, old);
    }

    [Fact]
    public void CostsOfCarrierByReferences_LeavesOutOtherCarriersAndOtherReferences()
    {
        var wanted = Cost("CB-0000001", "TRX-1", "CAR-0001");
        CarrierCost[] all = [wanted, Cost("CB-0000002", "TRX-1", "CAR-0002"), Cost("CB-0000003", "TRX-2", "CAR-0001")];

        new CarrierCostsOfCarrierByReferencesSpec(new CarrierId("CAR-0001"), ["TRX-1"]).Evaluate(all).Should().Equal(wanted);
    }

    [Fact]
    public void ToDecide_KeepsOnlyReceivedInvoices_TheOldestFirst()
    {
        var old = Invoice("CAR-0001", "A-1", Now);
        var recent = Invoice("CAR-0001", "A-2", Now.AddHours(1));
        var approved = CarrierInvoice.Receive(
            new CarrierInvoiceData(new CarrierId("CAR-0001"), "A-3", new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-9", 5m, null)], "invoice"),
            [Cost("CB-0000009", "TRX-9", "CAR-0001")],
            0m,
            Now).Invoice!;

        new CarrierInvoicesToDecideSpec().Evaluate([recent, approved, old]).Should().Equal(old, recent);
    }

    [Fact]
    public void CostsByBookingNumbers_FindsOnlyThoseCosts()
    {
        var wanted = Cost("CB-0000001", "TRX-1", "CAR-0001");
        CarrierCost[] all = [wanted, Cost("CB-0000002", "TRX-2", "CAR-0001")];

        new CarrierCostsByBookingNumbersSpec(["CB-0000001", "CB-0000007"]).Evaluate(all).Should().Equal(wanted);
    }
}

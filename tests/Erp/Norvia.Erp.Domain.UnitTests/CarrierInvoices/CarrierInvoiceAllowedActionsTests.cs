using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceAllowedActionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly CarrierId Carrier = new("CAR-0001");

    private static CarrierCost Cost(string number, string reference, decimal total) =>
        CarrierCost.Record(
            new CarrierCostData(
                number, reference, Carrier, "SH-0000001", "TO-0000001",
                new CarrierCostCharge(total, [new ChargeLine(ChargeLineType.Freight, total)]),
                CarrierCostSource.FromRateCard("CRC-0001", 1), Now, "delivered"),
            Now).Cost!;

    private static CarrierInvoiceLineData Line(string reference, decimal amount) => new(reference, amount, null);

    private static CarrierInvoice Received(CarrierCost[] costs, params CarrierInvoiceLineData[] lines) =>
        CarrierInvoice.Receive(new CarrierInvoiceData(Carrier, "VDT-1", new DateOnly(2026, 10, 19), lines, "invoice-1"), costs, 0m, Now).Invoice!;

    [Fact]
    public void AllowedActionsOf_APriceMismatchWithAnOpenCarrierCost_AllowsAcceptAcceptAdjustedAndReject()
    {
        var cost = Cost("CB-0000001", "TRX-1", 70m);
        var invoice = Received([cost], Line("TRX-1", 80m));

        invoice.AllowedActionsOf("TRX-1", cost).Should().Be(
            CarrierInvoiceLineActions.Accept | CarrierInvoiceLineActions.AcceptAdjusted | CarrierInvoiceLineActions.Reject);
    }

    [Fact]
    public void AllowedActionsOf_AnUnknownBooking_AllowsRejectAndWaitButNeverAccept()
    {
        var invoice = Received([], Line("TRX-9", 80m));

        invoice.AllowedActionsOf("TRX-9", null).Should().Be(CarrierInvoiceLineActions.Reject | CarrierInvoiceLineActions.Wait);
    }

    [Fact]
    public void AllowedActionsOf_ADuplicate_AllowsRejectOnly()
    {
        var cost = Cost("CB-0000001", "TRX-1", 70m);
        Received([cost], Line("TRX-1", 70m)); // invoices the cost
        var duplicate = Received([cost], Line("TRX-1", 70m));

        duplicate.Lines[0].Result.Should().Be(CarrierInvoiceLineResult.Duplicate);
        duplicate.AllowedActionsOf("TRX-1", cost).Should().Be(CarrierInvoiceLineActions.Reject);
    }

    [Fact]
    public void AllowedActionsOf_ALineThatIsDecided_AllowsNothing()
    {
        var invoice = Received([], Line("TRX-9", 80m), Line("TRX-8", 5m));
        invoice.Reject("TRX-9", "not ours", Now);

        invoice.AllowedActionsOf("TRX-9", null).Should().Be(CarrierInvoiceLineActions.None);
    }

    [Fact]
    public void AllowedActionsOf_APriceMismatch_AgreesWithWhatAcceptRefuses()
    {
        var cost = Cost("CB-0000001", "TRX-1", 70m);
        var invoice = Received([cost], Line("TRX-1", 80m));
        cost.MarkInvoiced();

        invoice.AllowedActionsOf("TRX-1", cost).Should().Be(CarrierInvoiceLineActions.Reject);
        invoice.Accept("TRX-1", null, null, cost, Now).Refusal.Should().NotBeNull();
    }
}

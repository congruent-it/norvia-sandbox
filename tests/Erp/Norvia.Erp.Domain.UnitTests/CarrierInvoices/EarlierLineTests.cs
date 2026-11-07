using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.UnitTests.CarrierInvoices;

public sealed class EarlierLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly CarrierId Carrier = new("CAR-0001");

    private static CarrierCost Cost() =>
        CarrierCost.Record(
            new CarrierCostData("CB-1", "TRX-1", Carrier, "SH-1", "TO-1", new CarrierCostCharge(70m, [new ChargeLine(ChargeLineType.Freight, 70m)]), CarrierCostSource.FromRateCard("RC", 3), Now, "c"),
            Now).Cost!;

    private static CarrierInvoice Invoice(string number, CarrierCost cost) =>
        CarrierInvoice.Receive(new CarrierInvoiceData(Carrier, number, new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-1", 70m, null)], number), [cost], 0m, Now).Invoice!;

    [Fact]
    public void Find_TheOldestLineThatInvoicedTheCost_IsChosen_NotTheLineBeingExplained()
    {
        var cost = Cost();
        var first = Invoice("A-1", cost);
        var second = Invoice("A-2", cost);

        var earlier = EarlierLine.Find([first, second], "CB-1", "A-2", "TRX-1");

        earlier!.Invoice.Should().BeSameAs(first);
    }

    [Fact]
    public void Find_WithNoMatchedOrAcceptedLine_IsNull()
    {
        EarlierLine.Find([], "CB-1", "A-2", "TRX-1").Should().BeNull();
        EarlierLine.Find([Invoice("A-1", Cost())], "CB-9", "A-2", "TRX-1").Should().BeNull();
    }

    [Fact]
    public void Label_OfASpotAndOfARateCard_IsTheLabelAndNeverTheRemark()
    {
        CarrierCostSource.FromSpot("secret remark").Label.Should().Be("Spot");
        CarrierCostSource.FromRateCard("RC", 3).Label.Should().Be("Rate Card version 3");
    }
}

using Ardalis.Specification;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.ShipperInvoices;

public sealed class ShipperInvoiceSpecsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static BillableLine Line(string number, string shipper, int day = 17) =>
        BillableLine.Record(
            new BillableLineData(
                number, new ShipperId(shipper), "PO", "SH-0000001", new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero),
                new FreightCharge(10m, [new ChargeLine(ChargeLineType.Freight, 10m)], "RC-0001", 1), false, "intake", "billing"),
            Now).Line!;

    private static ShipperInvoice Invoice(int sequence, DateTimeOffset issuedAt) =>
        ShipperInvoice.Issue(InvoiceNumber.Of(2026, sequence), new ShipperId("SHP-0001"), [Line("TO-000000" + sequence, "SHP-0001")], issuedAt, "corr");

    [Fact]
    public void WaitingBillableLinesToInvoice_TakesOnlyTheShippersWaitingLines()
    {
        var invoiced = Line("TO-0000003", "SHP-0001");
        invoiced.Invoice(InvoiceNumber.Of(2026, 1));
        List<BillableLine> lines = [Line("TO-0000002", "SHP-0001", day: 18), Line("TO-0000001", "SHP-0001"), Line("TO-0000004", "SHP-0002"), invoiced];

        var taken = new WaitingBillableLinesToInvoiceSpec(new ShipperId("SHP-0001")).Evaluate(lines);

        taken.Select(line => line.Id.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002");
    }

    [Fact]
    public void ShipperInvoicesNewestFirst_OrdersByIssueTimeDescending()
    {
        List<ShipperInvoice> invoices = [Invoice(1, Now), Invoice(3, Now.AddHours(2)), Invoice(2, Now.AddHours(1))];

        var ordered = new ShipperInvoicesNewestFirstSpec().Evaluate(invoices);

        ordered.Select(invoice => invoice.Id.Value).Should().Equal("INV-2026-000003", "INV-2026-000002", "INV-2026-000001");
    }

    [Fact]
    public void ShipperInvoiceByNumber_FindsTheInvoiceWithThatNumber()
    {
        List<ShipperInvoice> invoices = [Invoice(1, Now), Invoice(2, Now)];

        var found = new ShipperInvoiceByNumberSpec(InvoiceNumber.Of(2026, 2)).Evaluate(invoices);

        found.Should().ContainSingle().Which.Id.Should().Be(InvoiceNumber.Of(2026, 2));
    }

    [Fact]
    public void InvoiceNumberSequenceOfYear_FindsTheYearsSequence()
    {
        List<InvoiceNumberSequence> sequences = [new(2025), new(2026)];

        var found = new InvoiceNumberSequenceOfYearSpec(2026).Evaluate(sequences);

        found.Should().ContainSingle().Which.Year.Should().Be(2026);
    }
}

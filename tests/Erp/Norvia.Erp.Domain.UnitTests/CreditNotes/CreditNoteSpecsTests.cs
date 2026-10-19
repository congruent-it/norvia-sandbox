using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.CreditNotes;

public sealed class CreditNoteSpecsTests
{
    private static readonly ShipperId Shipper = new("SHP-0001");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static ShipperInvoice Invoice(int sequence)
    {
        var lines = new[] { Line("TO-0000001"), Line("TO-0000002") };
        return ShipperInvoice.Issue(InvoiceNumber.Of(2026, sequence), Shipper, lines, Now, "corr");
    }

    private static BillableLine Line(string number) =>
        BillableLine.Record(
            new BillableLineData(
                number, Shipper, "PO", "SH-0000001", Now,
                new FreightCharge(100m, [new ChargeLine(ChargeLineType.Freight, 100m)], "RC-0001", 1), false, "intake", "billing"),
            Now).Line!;

    private static CreditNote Note(int sequence, int invoice, string line, DateTimeOffset issuedAt) =>
        CreditNote.Issue(CreditNoteNumber.Of(2026, sequence), Invoice(invoice), line, 10m, "reason", 0m, issuedAt, "corr");

    [Fact]
    public void CreditNotesOfInvoiceLine_TakesOnlyTheCreditNotesOnThatLineOfThatInvoice()
    {
        List<CreditNote> notes = [Note(1, 1, "TO-0000001", Now), Note(2, 1, "TO-0000002", Now), Note(3, 2, "TO-0000001", Now), Note(4, 1, "TO-0000001", Now)];

        var taken = new CreditNotesOfInvoiceLineSpec(InvoiceNumber.Of(2026, 1), "TO-0000001").Evaluate(notes);

        taken.Select(note => note.Id.Value).Should().Equal("CN-2026-000001", "CN-2026-000004");
    }

    [Fact]
    public void CreditNotesNewestFirst_OrdersByIssueTimeDescending()
    {
        List<CreditNote> notes = [Note(1, 1, "TO-0000001", Now), Note(3, 2, "TO-0000001", Now.AddHours(2)), Note(2, 1, "TO-0000002", Now.AddHours(1))];

        var ordered = new CreditNotesNewestFirstSpec().Evaluate(notes);

        ordered.Select(note => note.Id.Value).Should().Equal("CN-2026-000003", "CN-2026-000002", "CN-2026-000001");
    }

    [Fact]
    public void CreditNotesNewestFirst_ForAnInvoice_TakesOnlyItsCreditNotes()
    {
        List<CreditNote> notes = [Note(1, 1, "TO-0000001", Now), Note(2, 2, "TO-0000001", Now.AddHours(1)), Note(3, 1, "TO-0000002", Now.AddHours(2))];

        var ordered = new CreditNotesNewestFirstSpec(InvoiceNumber.Of(2026, 1)).Evaluate(notes);

        ordered.Select(note => note.Id.Value).Should().Equal("CN-2026-000003", "CN-2026-000001");
    }

    [Fact]
    public void CreditNoteNumberSequenceOfYear_FindsTheSequenceOfThatYear()
    {
        List<CreditNoteNumberSequence> sequences = [new(2025), new(2026)];

        new CreditNoteNumberSequenceOfYearSpec(2026).Evaluate(sequences).Should().ContainSingle().Which.Year.Should().Be(2026);
    }
}

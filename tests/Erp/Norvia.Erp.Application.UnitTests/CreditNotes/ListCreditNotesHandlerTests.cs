using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.CreditNotes.List;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.CreditNotes;

public sealed class ListCreditNotesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly IReadRepository<CreditNote> _creditNotes = Substitute.For<IReadRepository<CreditNote>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();

    private static CreditNote Note(int sequence, string shipper)
    {
        var line = BillableLine.Record(
            new BillableLineData(
                "TO-0000001", new ShipperId(shipper), "PO", "SH-0000001", Now,
                new FreightCharge(100m, [new ChargeLine(ChargeLineType.Freight, 100m)], "RC-0001", 1), false, "intake", "billing"),
            Now).Line!;
        var invoice = ShipperInvoice.Issue(InvoiceNumber.Of(2026, 1), new ShipperId(shipper), [line], Now, "corr");
        return CreditNote.Issue(CreditNoteNumber.Of(2026, sequence), invoice, "TO-0000001", 10m, "reason", 0m, Now, "corr-" + sequence);
    }

    [Fact]
    public async Task List_ShowsEachCreditNoteWithItsShippersName_InTheOrderTheRepositoryGives()
    {
        _creditNotes.ListAsync(Arg.Any<ISpecification<CreditNote>>(), Arg.Any<CancellationToken>()).Returns([Note(2, "SHP-0001"), Note(1, "SHP-0404")]);
        _shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns([new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true)]);

        var views = await new ListCreditNotesHandler(_creditNotes, _shippers).Handle(new ListCreditNotesQuery(), CancellationToken.None);

        views.Select(view => (view.CreditNoteNumber, view.Shipper.Name)).Should().Equal(("CN-2026-000002", "Brouwer Dranken"), ("CN-2026-000001", "SHP-0404"));
    }

    [Fact]
    public async Task List_ForAnInvoice_AsksForThatInvoicesCreditNotes()
    {
        _creditNotes.ListAsync(Arg.Any<ISpecification<CreditNote>>(), Arg.Any<CancellationToken>()).Returns([]);
        _shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns([]);

        var views = await new ListCreditNotesHandler(_creditNotes, _shippers).Handle(new ListCreditNotesQuery("INV-2026-000001"), CancellationToken.None);

        views.Should().BeEmpty();
        await _creditNotes.Received(1).ListAsync(Arg.Any<CreditNotesNewestFirstSpec>(), Arg.Any<CancellationToken>());
    }
}

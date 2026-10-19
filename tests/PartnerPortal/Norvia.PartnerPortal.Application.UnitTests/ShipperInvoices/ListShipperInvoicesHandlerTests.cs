using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.ShipperInvoices.List;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Application.UnitTests.ShipperInvoices;

public sealed class ListShipperInvoicesHandlerTests
{
    [Fact]
    public async Task Handle_ReadsThePersonasInbox_WithTotalsLinesAndWhatACreditNoteCredits()
    {
        var credit = new CreditedLine("INV-2030-000001", "TO-0000002", "Rate Card error");
        var content = new ShipperInvoiceContent(
            InvoiceDocumentType.CreditNote, "CN-2030-000001", new DateOnly(2030, 11, 5), credit,
            [new ShipperInvoiceLine("TO-0000002", "BD-2030-0002", new DateOnly(2030, 10, 21), [new ShipperChargeLine(ChargeCode.MinimumChargeAdjustment, 14.50m)], 14.50m)],
            14.50m, 3.05m, 17.55m);
        var document = ShipperInvoice.Receive(Samples.BrouwerDranken.Id, "corr-1", Samples.Now, content);
        var repository = Substitute.For<IReadRepository<ShipperInvoice>>();
        repository.ListAsync(Arg.Any<ShipperInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([document]);

        var inbox = await new ListShipperInvoicesHandler(repository).Handle(new ListShipperInvoicesQuery(new PersonaId("brouwer-dranken")), CancellationToken.None);

        var view = inbox.Should().ContainSingle().Subject;
        view.DocumentNumber.Should().Be("CN-2030-000001");
        view.DocumentType.Should().Be(InvoiceDocumentType.CreditNote);
        view.ReceivedAt.Should().Be(Samples.Now);
        view.DocumentDate.Should().Be(new DateOnly(2030, 11, 5));
        view.Credit.Should().Be(credit);
        view.Lines.Should().BeSameAs(content.Lines);
        (view.TotalWithoutVat, view.Vat, view.TotalWithVat).Should().Be((14.50m, 3.05m, 17.55m));
        view.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void ShipperInvoicesOfPersonaSpec_IsTheLatestFirst()
    {
        var older = ShipperInvoice.Receive(Samples.BrouwerDranken.Id, "c", Samples.Now, Content("INV-2030-000001"));
        var newer = ShipperInvoice.Receive(Samples.BrouwerDranken.Id, "c", Samples.Now.AddHours(1), Content("INV-2030-000002"));
        var other = ShipperInvoice.Receive(new PersonaId("someone-else"), "c", Samples.Now.AddHours(2), Content("INV-2030-000003"));

        var listed = new ShipperInvoicesOfPersonaSpec(Samples.BrouwerDranken.Id).Evaluate([older, other, newer]);

        listed.Select(invoice => invoice.DocumentNumber).Should().Equal("INV-2030-000002", "INV-2030-000001");
    }

    [Fact]
    public void ShipperInvoiceByDocumentNumberSpec_FindsOnlyThePersonasOwnDocument()
    {
        var mine = ShipperInvoice.Receive(Samples.BrouwerDranken.Id, "c", Samples.Now, Content("INV-2030-000001"));
        var theirs = ShipperInvoice.Receive(new PersonaId("someone-else"), "c", Samples.Now, Content("INV-2030-000001"));
        var otherNumber = ShipperInvoice.Receive(Samples.BrouwerDranken.Id, "c", Samples.Now, Content("INV-2030-000002"));

        var found = new ShipperInvoiceByDocumentNumberSpec(Samples.BrouwerDranken.Id, "INV-2030-000001").Evaluate([mine, theirs, otherNumber]);

        found.Should().ContainSingle().Which.Should().BeSameAs(mine);
    }

    private static ShipperInvoiceContent Content(string number) => new(
        InvoiceDocumentType.Invoice, number, new DateOnly(2030, 11, 1), null,
        [new ShipperInvoiceLine("TO-0000001", "BD-2030-0001", new DateOnly(2030, 10, 18), [new ShipperChargeLine(ChargeCode.Freight, 80.00m)], 80.00m)],
        80.00m, 16.80m, 96.80m);
}

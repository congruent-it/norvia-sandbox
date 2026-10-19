using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.CreditNotes.Issue;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.CreditNotes;

public sealed class IssueCreditNoteHandlerTests
{
    private static readonly ShipperId Shipper = new("SHP-0001");
    private static readonly DateTimeOffset InvoicedAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(InvoicedAt.AddDays(3));
    private readonly IReadRepository<ShipperInvoice> _invoices = Substitute.For<IReadRepository<ShipperInvoice>>();
    private readonly IRepository<CreditNote> _creditNotes = Substitute.For<IRepository<CreditNote>>();
    private readonly IRepository<CreditNoteNumberSequence> _sequences = Substitute.For<IRepository<CreditNoteNumberSequence>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public IssueCreditNoteHandlerTests() =>
        _creditNotes.ListAsync(Arg.Any<ISpecification<CreditNote>>(), Arg.Any<CancellationToken>()).Returns([]);

    private IssueCreditNoteHandler Handler =>
        new(_invoices, _creditNotes, _sequences, _shippers, _outbox, _time, NullLogger<IssueCreditNoteHandler>.Instance);

    private static BillableLine Line(string number, decimal freight) =>
        BillableLine.Record(
            new BillableLineData(
                number, Shipper, "PO-" + number, "SH-" + number[3..], InvoicedAt,
                new FreightCharge(freight, [new ChargeLine(ChargeLineType.Freight, freight)], "RC-0001", 1), false, "intake", "billing"),
            InvoicedAt).Line!;

    private static ShipperInvoice Invoice() =>
        ShipperInvoice.Issue(InvoiceNumber.Of(2026, 1), Shipper, [Line("TO-0000001", 100m), Line("TO-0000002", 50m)], InvoicedAt, "invoice-corr");

    private static IssueCreditNoteCommand Command(decimal amount = 40m, string? reason = "Rate Card error on Lane NL-BE", string line = "TO-0000001") =>
        new("INV-2026-000001", line, amount, reason);

    private void HaveInvoice(ShipperInvoice? invoice) =>
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoice);

    private void HaveSequence(CreditNoteNumberSequence? sequence) =>
        _sequences.FirstOrDefaultAsync(Arg.Any<ISpecification<CreditNoteNumberSequence>>(), Arg.Any<CancellationToken>()).Returns(sequence);

    private void HaveEarlier(params decimal[] amounts)
    {
        var notes = amounts.Select((amount, index) =>
            CreditNote.Issue(CreditNoteNumber.Of(2026, index + 1), Invoice(), "TO-0000001", amount, "earlier", 0m, InvoicedAt, "earlier-corr")).ToList();
        _creditNotes.ListAsync(Arg.Any<ISpecification<CreditNote>>(), Arg.Any<CancellationToken>()).Returns(notes);
    }

    private IssueCreditNoteHandlerTests Ready()
    {
        HaveInvoice(Invoice());
        HaveSequence(new CreditNoteNumberSequence(2026));
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns(new Shipper(Shipper, "Brouwer Dranken", true));
        return this;
    }

    private IReadOnlyList<CreditNoteIssuedV1> Published() =>
        [.. _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<CreditNoteIssuedV1>()];

    [Fact]
    public async Task Handle_APartialCreditNote_IsIssuedOnTheLine_WithVatAndANumber()
    {
        Ready();

        var result = await Handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var note = result.Value;
        note.CreditNoteNumber.Should().Be("CN-2026-000001");
        note.CreditNoteDate.Should().Be(new DateOnly(2026, 10, 22));
        note.InvoiceNumber.Should().Be("INV-2026-000001");
        note.TransportOrderNumber.Should().Be("TO-0000001");
        note.Shipper.Should().Be(new ShipperOnInvoiceView("SHP-0001", "Brouwer Dranken"));
        (note.TotalWithoutVat, note.Vat, note.TotalWithVat).Should().Be((40m, 8.40m, 48.40m));
        note.Currency.Should().Be("EUR");
        note.Reason.Should().Be("Rate Card error on Lane NL-BE");
        await _creditNotes.Received(1).AddAsync(Arg.Is<CreditNote>(added => added.Id.Value == "CN-2026-000001"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ACreditNote_PublishesCreditNoteIssuedUnderANewCorrelationId()
    {
        Ready();

        var result = await Handler.Handle(Command(), CancellationToken.None);

        var published = Published().Should().ContainSingle().Subject;
        published.CreditNote.Should().Be(result.Value);
        published.CorrelationId.Should().NotBe("invoice-corr").And.Be(result.Value.CorrelationId);
        Guid.TryParse(published.CorrelationId, out _).Should().BeTrue();
        published.OccurredAt.Should().Be(_time.GetUtcNow());
        published.EventType.Should().Be("CreditNoteIssued");
        published.Topic.Should().Be(Topics.InvoicingEvents);
        published.ApplicationProperties[TrackingKeys.DocumentNumber].Should().Be("CN-2026-000001");
        published.ApplicationProperties[TrackingKeys.PartyId].Should().Be("SHP-0001");
        published.ApplicationProperties[TrackingKeys.Flow].Should().Be(Flows.Invoicing);
    }

    [Fact]
    public async Task Handle_TheEventBody_FollowsTheCanonicalModelsPropertyNames()
    {
        Ready();

        await Handler.Handle(Command(), CancellationToken.None);

        var body = System.Text.Json.JsonSerializer.SerializeToNode(Published().Single(), System.Text.Json.JsonSerializerOptions.Web)!.AsObject();
        body.Select(property => property.Key).Should().BeEquivalentTo("eventId", "eventType", "eventVersion", "occurredAt", "correlationId", "creditNote");
        body["creditNote"]!.AsObject().Select(property => property.Key).Should().BeEquivalentTo(
            "creditNoteNumber", "creditNoteDate", "shipper", "invoiceNumber", "transportOrderNumber", "shipperReference", "shipmentNumber",
            "deliveryDate", "currency", "totalWithoutVat", "vat", "totalWithVat", "reason", "issuedAt", "correlationId");
        body["creditNote"]!["creditNoteDate"]!.GetValue<string>().Should().Be("2026-10-22");
    }

    [Fact]
    public async Task Handle_TheFirstCreditNoteOfAYear_StartsItsSequenceInASaveOfItsOwn()
    {
        Ready();
        HaveSequence(null);

        var result = await Handler.Handle(Command(), CancellationToken.None);

        result.Value.CreditNoteNumber.Should().Be("CN-2026-000001");
        await _sequences.Received(1).AddAsync(Arg.Is<CreditNoteNumberSequence>(sequence => sequence.Year == 2026), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheNextCreditNote_TakesTheNextNumberOfTheSequence()
    {
        Ready();
        var sequence = new CreditNoteNumberSequence(2026);
        sequence.TakeNext();
        HaveSequence(sequence);

        var result = await Handler.Handle(Command(), CancellationToken.None);

        result.Value.CreditNoteNumber.Should().Be("CN-2026-000002");
        sequence.LastTaken.Should().Be(2);
        await _sequences.DidNotReceive().AddAsync(Arg.Any<CreditNoteNumberSequence>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AFullCreditNote_CreditsTheWholeLine()
    {
        Ready();

        var result = await Handler.Handle(Command(100m), CancellationToken.None);

        result.Value.TotalWithoutVat.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_WhatIsLeftAfterEarlierCreditNotes_IsAllowed()
    {
        Ready().HaveEarlier(60m, 10m);

        var result = await Handler.Handle(Command(30m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AboveTheLineTotalMinusEarlierCreditNotes_IsRefused_TakingNoNumberAndPublishingNothing()
    {
        Ready().HaveEarlier(60m, 10m);
        var sequence = new CreditNoteNumberSequence(2026);
        HaveSequence(sequence);

        var result = await Handler.Handle(Command(30.01m), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteAboveLineTotal");
        result.ValidationErrors.Single().ErrorMessage.Should().Contain("30.00");
        sequence.LastTaken.Should().Be(0);
        Published().Should().BeEmpty();
        await _creditNotes.DidNotReceive().AddAsync(Arg.Any<CreditNote>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Handle_WithoutAReason_IsRefusedWithCreditNoteReasonRequired(string? reason)
    {
        Ready();

        var result = await Handler.Handle(Command(reason: reason), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteReasonRequired");
        result.ValidationErrors.Single().ErrorMessage.Should().Be("A Credit note needs a reason.");
        Published().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ALineTheInvoiceDoesNotHave_IsRefusedWithInvoiceLineNotFound()
    {
        Ready();

        var result = await Handler.Handle(Command(line: "TO-0000099"), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("InvoiceLineNotFound");
    }

    [Theory]
    [InlineData("INV-2026-999999")]
    [InlineData("")]
    public async Task Handle_AnUnknownInvoice_IsRefusedWithShipperInvoiceNotFound(string number)
    {
        HaveInvoice(null);

        var result = await Handler.Handle(Command() with { InvoiceNumber = number }, CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("ShipperInvoiceNotFound");
        Published().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnUnknownShipper_ShowsTheShipperId()
    {
        Ready();
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns((Shipper?)null);

        var result = await Handler.Handle(Command(), CancellationToken.None);

        result.Value.Shipper.Name.Should().Be("SHP-0001");
    }

    [Fact]
    public async Task Handle_TheInvoice_IsNeverChanged()
    {
        var invoice = Invoice();
        Ready();
        HaveInvoice(invoice);
        var before = (invoice.TotalWithoutVat, invoice.Vat, invoice.TotalWithVat, invoice.Status);

        await Handler.Handle(Command(), CancellationToken.None);

        (invoice.TotalWithoutVat, invoice.Vat, invoice.TotalWithVat, invoice.Status).Should().Be(before);
        _invoices.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(IReadRepository<ShipperInvoice>.FirstOrDefaultAsync));
    }
}

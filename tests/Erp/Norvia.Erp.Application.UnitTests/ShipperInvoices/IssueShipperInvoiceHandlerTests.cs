using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.Issue;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.ShipperInvoices;

public sealed class IssueShipperInvoiceHandlerTests
{
    private static readonly ShipperId ShipperId = new("SHP-0001");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
    private readonly IRepository<BillableLine> _lines = Substitute.For<IRepository<BillableLine>>();
    private readonly IRepository<InvoiceNumberSequence> _sequences = Substitute.For<IRepository<InvoiceNumberSequence>>();
    private readonly IRepository<ShipperInvoice> _invoices = Substitute.For<IRepository<ShipperInvoice>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private IssueShipperInvoiceHandler Handler => new(_lines, _sequences, _invoices, _shippers, _outbox, _time, NullLogger<IssueShipperInvoiceHandler>.Instance);

    private static BillableLine Line(string number, decimal freight, decimal adjustment = 0m)
    {
        List<ChargeLine> chargeLines = [new(ChargeLineType.Freight, freight)];
        if (adjustment > 0m)
        {
            chargeLines.Add(new ChargeLine(ChargeLineType.MinimumChargeAdjustment, adjustment));
        }

        return BillableLine.Record(
            new BillableLineData(
                number, ShipperId, "PO-" + number, "SH-" + number[3..], new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
                new FreightCharge(freight + adjustment, chargeLines, "RC-0001", 1), false, "intake", "billing"),
            new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero)).Line!;
    }

    private void KnowShipper() =>
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns(new Shipper(ShipperId, "Brouwer Dranken", true));

    private void HaveWaiting(params BillableLine[] lines) =>
        _lines.ListAsync(Arg.Any<ISpecification<BillableLine>>(), Arg.Any<CancellationToken>()).Returns(lines.ToList());

    private void HaveSequence(InvoiceNumberSequence sequence) =>
        _sequences.FirstOrDefaultAsync(Arg.Any<ISpecification<InvoiceNumberSequence>>(), Arg.Any<CancellationToken>()).Returns(sequence);

    [Fact]
    public async Task Handle_WaitingLines_IssuesOneInvoiceWithALinePerTransportOrder()
    {
        KnowShipper();
        var lines = new[] { Line("TO-0000001", 100m), Line("TO-0000002", 50m, 10m) };
        HaveWaiting(lines);
        HaveSequence(new InvoiceNumberSequence(2026));

        var result = await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var invoice = result.Value.Invoice!;
        invoice.InvoiceNumber.Should().Be("INV-2026-000001");
        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 10, 19));
        invoice.Shipper.Should().Be(new ShipperOnInvoiceView("SHP-0001", "Brouwer Dranken"));
        invoice.Lines.Select(line => line.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002");
        invoice.Lines[1].ChargeLines.Select(chargeLine => chargeLine.Type).Should().Equal("Freight", "MinimumChargeAdjustment");
        invoice.Lines[0].DeliveryDate.Should().Be(new DateOnly(2026, 10, 17));
        (invoice.TotalWithoutVat, invoice.Vat, invoice.TotalWithVat).Should().Be((160m, 33.60m, 193.60m));
        invoice.Currency.Should().Be("EUR");
        invoice.Status.Should().Be("Issued");
        lines.Should().OnlyContain(line => line.Status == BillableLineStatus.Invoiced && line.InvoiceNumber!.Value.Value == "INV-2026-000001");
        await _invoices.Received(1).AddAsync(Arg.Is<ShipperInvoice>(added => added.Id.Value == "INV-2026-000001"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WaitingLines_PublishesShipperInvoiceIssuedWithTheWholeInvoiceUnderANewCorrelationId()
    {
        KnowShipper();
        HaveWaiting(Line("TO-0000001", 100m));
        HaveSequence(new InvoiceNumberSequence(2026));

        var result = await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None);

        var invoice = result.Value.Invoice!;
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<ShipperInvoiceIssuedV1>().Should().ContainSingle().Subject;
        published.Invoice.Should().Be(invoice);
        published.CorrelationId.Should().Be(invoice.CorrelationId).And.NotBeNullOrWhiteSpace();
        published.CorrelationId.Should().NotBe("intake").And.NotBe("billing");
        published.OccurredAt.Should().Be(_time.GetUtcNow());
        published.EventType.Should().Be("ShipperInvoiceIssued");
        published.Topic.Should().Be("invoicing-events");
        published.ApplicationProperties.Should().Contain(new KeyValuePair<string, object>("partyId", "SHP-0001"))
            .And.Contain(new KeyValuePair<string, object>("flow", "invoicing"))
            .And.Contain(new KeyValuePair<string, object>("correlationId", invoice.CorrelationId));
    }

    [Fact]
    public async Task Handle_TwoRuns_UseDifferentCorrelationIds()
    {
        KnowShipper();
        HaveSequence(new InvoiceNumberSequence(2026));
        HaveWaiting(Line("TO-0000001", 10m));
        var first = (await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None)).Value.Invoice!;
        HaveWaiting(Line("TO-0000002", 10m));

        var second = (await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None)).Value.Invoice!;

        second.CorrelationId.Should().NotBe(first.CorrelationId);
        second.InvoiceNumber.Should().Be("INV-2026-000002");
    }

    [Fact]
    public async Task Handle_AShipperWithoutWaitingLines_IssuesNothing()
    {
        KnowShipper();
        HaveWaiting();

        var result = await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoice.Should().BeNull();
        await _invoices.DidNotReceive().AddAsync(Arg.Any<ShipperInvoice>(), Arg.Any<CancellationToken>());
        await _sequences.DidNotReceive().AddAsync(Arg.Any<InvoiceNumberSequence>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_AnUnknownShipper_IsNotFound()
    {
        var result = await Handler.Handle(new IssueShipperInvoiceCommand("SHP-9999"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _invoices.DidNotReceive().AddAsync(Arg.Any<ShipperInvoice>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_TheFirstRunOfAYear_StartsThatYearsSequence()
    {
        KnowShipper();
        HaveWaiting(Line("TO-0000001", 10m));
        _sequences.FirstOrDefaultAsync(Arg.Any<ISpecification<InvoiceNumberSequence>>(), Arg.Any<CancellationToken>()).Returns((InvoiceNumberSequence?)null);
        _time.SetUtcNow(new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));

        var result = await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None);

        result.Value.Invoice!.InvoiceNumber.Should().Be("INV-2027-000001");
        await _sequences.Received(1).AddAsync(Arg.Is<InvoiceNumberSequence>(added => added.Year == 2027), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARunPastMidnightInAmsterdam_NumbersByTheAmsterdamYear()
    {
        KnowShipper();
        HaveWaiting(Line("TO-0000001", 10m));
        HaveSequence(new InvoiceNumberSequence(2027));
        _time.SetUtcNow(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero));

        var result = await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None);

        result.Value.Invoice!.InvoiceNumber.Should().Be("INV-2027-000001");
        result.Value.Invoice.InvoiceDate.Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public async Task Handle_ASequenceWithNumbersTaken_ContinuesAfterTheLastOne()
    {
        KnowShipper();
        HaveWaiting(Line("TO-0000001", 10m));
        var sequence = new InvoiceNumberSequence(2026);
        sequence.TakeNext();
        sequence.TakeNext();
        HaveSequence(sequence);

        var result = await Handler.Handle(new IssueShipperInvoiceCommand("SHP-0001"), CancellationToken.None);

        result.Value.Invoice!.InvoiceNumber.Should().Be("INV-2026-000003");
        sequence.LastTaken.Should().Be(3);
        await _sequences.DidNotReceive().AddAsync(Arg.Any<InvoiceNumberSequence>(), Arg.Any<CancellationToken>());
    }
}

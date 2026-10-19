using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.SendAgain;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.ShipperInvoices;

public sealed class SendShipperInvoiceAgainHandlerTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly IRepository<ShipperInvoice> _invoices = Substitute.For<IRepository<ShipperInvoice>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly FakeTimeProvider _time = new(IssuedAt.AddHours(1));

    private SendShipperInvoiceAgainHandler Handler => new(_invoices, _shippers, _outbox, _time, NullLogger<SendShipperInvoiceAgainHandler>.Instance);

    private static ShipperInvoice Invoice()
    {
        var line = BillableLine.Record(
            new BillableLineData(
                "TO-0000001", new ShipperId("SHP-0001"), "PO", "SH-0000001", IssuedAt,
                new FreightCharge(10m, [new ChargeLine(ChargeLineType.Freight, 10m)], "RC-0001", 1), false, "intake", "billing"),
            IssuedAt).Line!;
        return ShipperInvoice.Issue(InvoiceNumber.Of(2026, 1), new ShipperId("SHP-0001"), [line], IssuedAt, "corr-1");
    }

    private ShipperInvoice Have(ShipperInvoice invoice)
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoice);
        return invoice;
    }

    private static ShipperInvoice NotDelivered()
    {
        var invoice = Invoice();
        invoice.ReportNotDelivered(500, "cause", IssuedAt.AddMinutes(1));
        return invoice;
    }

    [Fact]
    public async Task Handle_ANotDeliveredInvoice_IsIssuedOnceMore_AndPublishesTheSameInvoiceAgain()
    {
        var invoice = Have(NotDelivered());

        var result = await Handler.Handle(new SendShipperInvoiceAgainCommand("INV-2026-000001"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(ShipperInvoiceStatus.Issued);
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<ShipperInvoiceIssuedV1>().Should().ContainSingle().Subject;
        published.CorrelationId.Should().Be("corr-1");
        published.OccurredAt.Should().Be(IssuedAt.AddHours(1));
        published.Invoice.InvoiceNumber.Should().Be("INV-2026-000001");
        published.Invoice.Status.Should().Be("Issued");
        published.Invoice.NotDelivered.Should().BeNull();
        published.Invoice.TotalWithVat.Should().Be(12.10m);
        await _invoices.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheShippersName_IsTakenFromTheShipperData()
    {
        Have(NotDelivered());
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));

        var result = await Handler.Handle(new SendShipperInvoiceAgainCommand("INV-2026-000001"), CancellationToken.None);

        result.Value.Shipper.Name.Should().Be("Brouwer Dranken");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_AnInvoiceThatIsNotNotDelivered_IsRefused_PublishingNothing(bool sent)
    {
        var invoice = Have(Invoice());
        if (sent)
        {
            invoice.ReportSent(IssuedAt.AddMinutes(1));
        }

        var result = await Handler.Handle(new SendShipperInvoiceAgainCommand("INV-2026-000001"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("ShipperInvoiceNotSendableAgain");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _invoices.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_SendingAgainTwice_PublishesOnce()
    {
        Have(NotDelivered());
        await Handler.Handle(new SendShipperInvoiceAgainCommand("INV-2026-000001"), CancellationToken.None);

        var second = await Handler.Handle(new SendShipperInvoiceAgainCommand("INV-2026-000001"), CancellationToken.None);

        second.Status.Should().Be(ResultStatus.Invalid);
        _outbox.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData("INV-2026-999999")]
    [InlineData("")]
    public async Task Handle_AnUnknownInvoice_IsRefusedWithShipperInvoiceNotFound(string number)
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns((ShipperInvoice?)null);

        var result = await Handler.Handle(new SendShipperInvoiceAgainCommand(number), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("ShipperInvoiceNotFound");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }
}

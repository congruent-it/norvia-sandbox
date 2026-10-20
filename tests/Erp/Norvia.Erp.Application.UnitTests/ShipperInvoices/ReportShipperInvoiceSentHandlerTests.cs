using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;
using Norvia.Erp.Application.UnitTests.BillableLines;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.ShipperInvoices;

public sealed class ReportShipperInvoiceSentHandlerTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly IRepository<ShipperInvoice> _invoices = Substitute.For<IRepository<ShipperInvoice>>();
    private readonly FakeTimeProvider _time = new(IssuedAt.AddMinutes(1));

    private ReportShipperInvoiceSentHandler Handler => new(_invoices, _time);

    private static ShipperInvoice Invoice()
    {
        var line = BillableLine.Record(
            new BillableLineData(
                "TO-0000001", new ShipperId("SHP-0001"), "PO", "SH-0000001", IssuedAt,
                new FreightCharge(10m, [new ChargeLine(ChargeLineType.Freight, 10m)], "RC-0001", 1), false, "intake", "billing"),
            IssuedAt).Line!;
        return ShipperInvoice.Issue(InvoiceNumber.Of(2026, 1), new ShipperId("SHP-0001"), [line], IssuedAt, "corr-1");
    }

    private ShipperInvoice HaveIssued()
    {
        var invoice = Invoice();
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoice);
        return invoice;
    }

    [Fact]
    public async Task Handle_AnIssuedInvoice_IsSentAtTheTimeOfTheReport_AndSaved()
    {
        var invoice = HaveIssued();

        var result = await Handler.Handle(new ReportShipperInvoiceSentCommand("INV-2026-000001", "corr-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ShipperInvoiceReportResult("INV-2026-000001", "Sent", IssuedAt.AddMinutes(1)));
        invoice.Status.Should().Be(ShipperInvoiceStatus.Sent);
        await _invoices.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnInvoiceReportedAlready_IsIgnored_KeepingTheFirstTimeAndSavingNothing()
    {
        var invoice = HaveIssued();
        await Handler.Handle(new ReportShipperInvoiceSentCommand("INV-2026-000001", "corr-1"), CancellationToken.None);
        _invoices.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromHours(1));

        var result = await Handler.Handle(new ReportShipperInvoiceSentCommand("INV-2026-000001", "corr-1"), CancellationToken.None);

        result.Value.Should().Be(new ShipperInvoiceReportResult("INV-2026-000001", "Sent", IssuedAt.AddMinutes(1), Ignored: true));
        invoice.SentAt.Should().Be(IssuedAt.AddMinutes(1));
        await _invoices.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("INV-2026-999999")]
    [InlineData("")]
    public async Task Handle_AnUnknownInvoice_IsRefusedWithShipperInvoiceNotFound(string number)
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns((ShipperInvoice?)null);

        var result = await Handler.Handle(new ReportShipperInvoiceSentCommand(number, "corr-1"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("ShipperInvoiceNotFound");
        await _invoices.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Track_AReport_IsOnTheTrailOfTheInvoice_AndSucceeds()
    {
        HaveIssued();
        var logger = new CapturingLogger<TrackShipperInvoiceReportInLoggingScope>();

        await new TrackShipperInvoiceReportInLoggingScope(logger).Handle(
            new ReportShipperInvoiceSentCommand("INV-2026-000001", "corr-1"), (message, token) => Handler.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("correlationId") == "corr-1" && (string?)scope.GetValueOrDefault("documentNumber") == "INV-2026-000001");
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("reported Sent");
    }

    [Fact]
    public async Task Track_AReportForAnInvoiceReportedAlready_IsLoggedAsIgnored()
    {
        HaveIssued();
        await Handler.Handle(new ReportShipperInvoiceSentCommand("INV-2026-000001", "corr-1"), CancellationToken.None);
        var logger = new CapturingLogger<TrackShipperInvoiceReportInLoggingScope>();

        await new TrackShipperInvoiceReportInLoggingScope(logger).Handle(
            new ReportShipperInvoiceSentCommand("INV-2026-000001", "corr-1"), (message, token) => Handler.Handle(message, token), CancellationToken.None);

        logger.Messages.Should().ContainSingle().Which.Should().Contain("was reported already").And.Contain("ignored");
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
    }

    [Fact]
    public async Task Track_AReportForAnUnknownInvoice_RecordsRefusedWithItsFailureCode()
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns((ShipperInvoice?)null);
        var logger = new CapturingLogger<TrackShipperInvoiceReportInLoggingScope>();

        await new TrackShipperInvoiceReportInLoggingScope(logger).Handle(
            new ReportShipperInvoiceSentCommand("INV-2026-999999", "corr-1"), (message, token) => Handler.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Refused" && (string?)scope.GetValueOrDefault("failureCode") == "ShipperInvoiceNotFound");
    }

    [Fact]
    public async Task View_AnInvoiceListedAfterItWasReported_CarriesTheSentTime()
    {
        var invoice = Invoice();
        invoice.ReportSent(IssuedAt.AddMinutes(1));
        var shippers = Substitute.For<IReadRepository<Shipper>>();
        var invoices = Substitute.For<IReadRepository<ShipperInvoice>>();
        invoices.ListAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns([invoice, Invoice()]);
        shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns([]);

        var views = await new Norvia.Erp.Application.ShipperInvoices.List.ListShipperInvoicesHandler(invoices, shippers)
            .Handle(new Norvia.Erp.Application.ShipperInvoices.List.ListShipperInvoicesQuery(), CancellationToken.None);

        views[0].Should().Match<ShipperInvoiceView>(view => view.Status == "Sent" && view.SentAt == IssuedAt.AddMinutes(1));
        views[1].Should().Match<ShipperInvoiceView>(view => view.Status == "Issued" && view.SentAt == null);
    }
}

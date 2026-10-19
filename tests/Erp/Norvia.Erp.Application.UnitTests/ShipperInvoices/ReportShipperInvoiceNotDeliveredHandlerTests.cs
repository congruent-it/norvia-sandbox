using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;
using Norvia.Erp.Application.UnitTests.BillableLines;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.ShipperInvoices;

public sealed class ReportShipperInvoiceNotDeliveredHandlerTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly IRepository<ShipperInvoice> _invoices = Substitute.For<IRepository<ShipperInvoice>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly FakeTimeProvider _time = new(IssuedAt.AddMinutes(1));

    private ReportShipperInvoiceNotDeliveredHandler Handler => new(_invoices, _outbox, _time);

    private static ReportShipperInvoiceNotDeliveredCommand Report(string number = "INV-2026-000001") => new(number, "corr-1", 503, "Overloaded");

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
    public async Task Handle_AnIssuedInvoice_IsNotDelivered_AndPublishesTheEventWithTheInvoicesCorrelationId()
    {
        var invoice = HaveIssued();

        var result = await Handler.Handle(Report(), CancellationToken.None);

        result.Value.Should().Be(new ShipperInvoiceReportResult("INV-2026-000001", "NotDelivered", null));
        invoice.Status.Should().Be(ShipperInvoiceStatus.NotDelivered);
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<ShipperInvoiceNotDeliveredV1>().Should().ContainSingle().Subject;
        published.CorrelationId.Should().Be("corr-1");
        published.DocumentType.Should().Be("INVOICE");
        published.DocumentNumber.Should().Be("INV-2026-000001");
        published.ShipperId.Should().Be("SHP-0001");
        published.StatusCode.Should().Be(503);
        published.Message.Should().Be("Overloaded");
        published.OccurredAt.Should().Be(IssuedAt.AddMinutes(1));
        published.Topic.Should().Be("invoicing-events");
        published.EventType.Should().Be("ShipperInvoiceNotDelivered");
        published.ApplicationProperties.Should().Contain(new KeyValuePair<string, object>("partyId", "SHP-0001"))
            .And.Contain(new KeyValuePair<string, object>("documentNumber", "INV-2026-000001"))
            .And.Contain(new KeyValuePair<string, object>("flow", "invoicing"));
        await _invoices.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnInvoiceSentAlready_IsIgnored_PublishingAndSavingNothing()
    {
        var invoice = HaveIssued();
        invoice.ReportSent(IssuedAt.AddMinutes(1));

        var result = await Handler.Handle(Report(), CancellationToken.None);

        result.Value.Should().Be(new ShipperInvoiceReportResult("INV-2026-000001", "Sent", IssuedAt.AddMinutes(1), Ignored: true));
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _invoices.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_ARepeatedReport_IsIgnored_AndPublishesOnce()
    {
        HaveIssued();
        await Handler.Handle(Report(), CancellationToken.None);
        _invoices.ClearReceivedCalls();

        var result = await Handler.Handle(Report(), CancellationToken.None);

        result.Value.Ignored.Should().BeTrue();
        _outbox.ReceivedCalls().Should().ContainSingle();
        await _invoices.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("INV-2026-999999")]
    [InlineData("")]
    public async Task Handle_AnUnknownInvoice_IsRefusedWithShipperInvoiceNotFound(string number)
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns((ShipperInvoice?)null);

        var result = await Handler.Handle(Report(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("ShipperInvoiceNotFound");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Track_AReport_IsOnTheTrailOfTheInvoice_AndSucceeds()
    {
        HaveIssued();
        var logger = new CapturingLogger<TrackShipperInvoiceNotDeliveredReportInLoggingScope>();

        await new TrackShipperInvoiceNotDeliveredReportInLoggingScope(logger).Handle(Report(), (message, token) => Handler.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("correlationId") == "corr-1" && (string?)scope.GetValueOrDefault("documentNumber") == "INV-2026-000001");
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("Not delivered").And.Contain("Overloaded");
    }

    [Fact]
    public async Task Track_ALateReport_IsLoggedAsIgnored()
    {
        HaveIssued().ReportSent(IssuedAt);
        var logger = new CapturingLogger<TrackShipperInvoiceNotDeliveredReportInLoggingScope>();

        await new TrackShipperInvoiceNotDeliveredReportInLoggingScope(logger).Handle(Report(), (message, token) => Handler.Handle(message, token), CancellationToken.None);

        logger.Messages.Should().ContainSingle().Which.Should().Contain("is Sent").And.Contain("ignored");
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
    }

    [Fact]
    public async Task Track_AReportForAnUnknownInvoice_RecordsRefusedWithItsFailureCode()
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns((ShipperInvoice?)null);
        var logger = new CapturingLogger<TrackShipperInvoiceNotDeliveredReportInLoggingScope>();

        await new TrackShipperInvoiceNotDeliveredReportInLoggingScope(logger).Handle(Report("INV-2026-999999"), (message, token) => Handler.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Refused" && (string?)scope.GetValueOrDefault("failureCode") == "ShipperInvoiceNotFound");
    }

    [Fact]
    public async Task View_AnInvoiceListedAfterItWasReportedNotDelivered_CarriesTheCause()
    {
        var invoice = Invoice();
        invoice.ReportNotDelivered(null, "timed out", IssuedAt.AddMinutes(1));
        var shippers = Substitute.For<IReadRepository<Shipper>>();
        var invoices = Substitute.For<IReadRepository<ShipperInvoice>>();
        invoices.ListAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns([invoice]);
        shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns([]);

        var views = await new Norvia.Erp.Application.ShipperInvoices.List.ListShipperInvoicesHandler(invoices, shippers)
            .Handle(new Norvia.Erp.Application.ShipperInvoices.List.ListShipperInvoicesQuery(), CancellationToken.None);

        views.Single().Status.Should().Be("NotDelivered");
        views.Single().NotDelivered.Should().Be(new NotDeliveredView(IssuedAt.AddMinutes(1), null, "timed out"));
    }
}

using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.ReportNotDelivered;
using Norvia.Erp.Application.CarrierInvoices.ReportTold;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.UnitTests.BillableLines;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.UnitTests.CarrierInvoices;

public sealed class ReportCarrierInvoiceOutcomeHandlerTests
{
    private static readonly DateTimeOffset ApprovedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly CarrierId Carrier = new("CAR-0001");

    private readonly IRepository<CarrierInvoice> _invoices = Substitute.For<IRepository<CarrierInvoice>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly FakeTimeProvider _time = new(ApprovedAt.AddMinutes(1));

    private ReportCarrierInvoiceToldHandler Told => new(_invoices, _time);

    private ReportCarrierInvoiceOutcomeNotDeliveredHandler NotDelivered => new(_invoices, _outbox, _time);

    private static ReportCarrierInvoiceToldCommand ToldReport(string carrierId = "CAR-0001", string number = "VDT-1") => new(carrierId, number, "corr-1");

    private static ReportCarrierInvoiceOutcomeNotDeliveredCommand NotDeliveredReport(string carrierId = "CAR-0001", string number = "VDT-1") =>
        new(carrierId, number, "corr-1", 503, "Overloaded");

    private static CarrierCost Cost() =>
        CarrierCost.Record(
            new CarrierCostData(
                "CB-0000001", "TRX-1", Carrier, "SH-0000001", "TO-0000001",
                new CarrierCostCharge(70m, [new ChargeLine(ChargeLineType.Freight, 70m)]), CarrierCostSource.FromSpot("remark"), ApprovedAt, "delivered"),
            ApprovedAt).Cost!;

    private static CarrierInvoiceData Data(decimal amount) =>
        new(Carrier, "VDT-1", new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-1", amount, null)], "corr-1");

    private CarrierInvoice HaveApproved()
    {
        var invoice = CarrierInvoice.Receive(Data(70m), [Cost()], 0m, ApprovedAt).Invoice!;
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoice);
        return invoice;
    }

    private void HaveNoInvoice() =>
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns((CarrierInvoice?)null);

    [Fact]
    public async Task Told_AnApprovedInvoice_RecordsTheTimeAndPublishesNothing()
    {
        var invoice = HaveApproved();

        var result = await Told.Handle(ToldReport(), CancellationToken.None);

        result.Value.Should().Be(new CarrierInvoiceReportResult("CAR-0001", "VDT-1", ApprovedAt.AddMinutes(1)));
        invoice.ToldAt.Should().Be(ApprovedAt.AddMinutes(1));
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _invoices.Received(1).UpdateAsync(invoice, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Told_ARepeatedReport_IsIgnored_ChangingNothing()
    {
        HaveApproved();
        await Told.Handle(ToldReport(), CancellationToken.None);
        _invoices.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await Told.Handle(ToldReport(), CancellationToken.None);

        result.Value.Should().Be(new CarrierInvoiceReportResult("CAR-0001", "VDT-1", ApprovedAt.AddMinutes(1), Ignored: true));
        await _invoices.DidNotReceiveWithAnyArgs().UpdateAsync(null!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Told_AnInvoiceFlaggedCarrierNotTold_IsIgnored()
    {
        var invoice = HaveApproved();
        invoice.ReportNotDelivered(500, "boom", ApprovedAt);

        var result = await Told.Handle(ToldReport(), CancellationToken.None);

        result.Value.Ignored.Should().BeTrue();
        invoice.ToldAt.Should().BeNull();
    }

    [Fact]
    public async Task Told_AnInvoiceThatIsNotApproved_IsIgnored()
    {
        var invoice = CarrierInvoice.Receive(Data(80m), [Cost()], 0m, ApprovedAt).Invoice!;
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoice);

        var result = await Told.Handle(ToldReport(), CancellationToken.None);

        result.Value.Ignored.Should().BeTrue();
        invoice.ToldAt.Should().BeNull();
    }

    [Theory]
    [InlineData("CAR-0001", "VDT-9")]
    [InlineData("", "VDT-1")]
    [InlineData("CAR-0001", "")]
    public async Task Told_AnUnknownInvoice_IsRefusedWithCarrierInvoiceNotFound(string carrierId, string number)
    {
        HaveNoInvoice();

        var result = await Told.Handle(ToldReport(carrierId, number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceNotFound");
    }

    [Fact]
    public async Task NotDelivered_AnApprovedInvoice_IsFlaggedAndPublishesTheEventWithTheInvoicesCorrelationId()
    {
        var invoice = HaveApproved();

        var result = await NotDelivered.Handle(NotDeliveredReport(), CancellationToken.None);

        result.Value.Should().Be(new CarrierInvoiceReportResult("CAR-0001", "VDT-1", null));
        invoice.NotToldAt.Should().Be(ApprovedAt.AddMinutes(1));
        invoice.NotToldStatusCode.Should().Be(503);
        invoice.NotToldMessage.Should().Be("Overloaded");
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<CarrierInvoiceOutcomeNotDeliveredV1>().Should().ContainSingle().Subject;
        published.CorrelationId.Should().Be("corr-1");
        published.CarrierId.Should().Be("CAR-0001");
        published.InvoiceNumber.Should().Be("VDT-1");
        published.StatusCode.Should().Be(503);
        published.Message.Should().Be("Overloaded");
        published.OccurredAt.Should().Be(ApprovedAt.AddMinutes(1));
        published.Topic.Should().Be("invoicing-events");
        published.EventType.Should().Be("CarrierInvoiceOutcomeNotDelivered");
        published.ApplicationProperties.Should().Contain(new KeyValuePair<string, object>("partyId", "CAR-0001"))
            .And.Contain(new KeyValuePair<string, object>("documentNumber", "VDT-1"))
            .And.Contain(new KeyValuePair<string, object>("flow", "carrier-invoices"));
        await _invoices.Received(1).UpdateAsync(invoice, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotDelivered_ACauseOverTheLimit_IsKeptToFiveHundredCharacters()
    {
        var invoice = HaveApproved();

        await NotDelivered.Handle(NotDeliveredReport() with { Message = new string('x', 600) }, CancellationToken.None);

        invoice.NotToldMessage.Should().HaveLength(500);
    }

    [Fact]
    public async Task NotDelivered_AnInvoiceTheCarrierWasToldAlready_IsIgnored_PublishingAndSavingNothing()
    {
        var invoice = HaveApproved();
        invoice.ReportTold(ApprovedAt.AddMinutes(1));

        var result = await NotDelivered.Handle(NotDeliveredReport(), CancellationToken.None);

        result.Value.Should().Be(new CarrierInvoiceReportResult("CAR-0001", "VDT-1", ApprovedAt.AddMinutes(1), Ignored: true));
        invoice.NotToldAt.Should().BeNull();
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _invoices.DidNotReceiveWithAnyArgs().UpdateAsync(null!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NotDelivered_ARepeatedReport_IsIgnored_AndPublishesOnce()
    {
        HaveApproved();
        await NotDelivered.Handle(NotDeliveredReport(), CancellationToken.None);
        _invoices.ClearReceivedCalls();

        var result = await NotDelivered.Handle(NotDeliveredReport(), CancellationToken.None);

        result.Value.Ignored.Should().BeTrue();
        _outbox.ReceivedCalls().Should().ContainSingle();
        await _invoices.DidNotReceiveWithAnyArgs().UpdateAsync(null!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NotDelivered_AnInvoiceThatIsNotApproved_IsIgnored()
    {
        var invoice = CarrierInvoice.Receive(Data(80m), [Cost()], 0m, ApprovedAt).Invoice!;
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoice);

        var result = await NotDelivered.Handle(NotDeliveredReport(), CancellationToken.None);

        result.Value.Ignored.Should().BeTrue();
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Theory]
    [InlineData("CAR-0001", "VDT-9")]
    [InlineData("", "VDT-1")]
    [InlineData("CAR-0001", "")]
    public async Task NotDelivered_AnUnknownInvoice_IsRefusedWithCarrierInvoiceNotFound(string carrierId, string number)
    {
        HaveNoInvoice();

        var result = await NotDelivered.Handle(NotDeliveredReport(carrierId, number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceNotFound");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task TrackTold_AReport_IsOnTheTrailOfTheCarrierInvoice_AndSucceeds()
    {
        HaveApproved();
        var logger = new CapturingLogger<TrackCarrierInvoiceToldReportInLoggingScope>();

        await new TrackCarrierInvoiceToldReportInLoggingScope(logger).Handle(ToldReport(), (message, token) => Told.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope =>
            (string?)scope.GetValueOrDefault("correlationId") == "corr-1" && (string?)scope.GetValueOrDefault("documentNumber") == "VDT-1" && (string?)scope.GetValueOrDefault("partyId") == "CAR-0001");
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("Carrier told");
    }

    [Fact]
    public async Task TrackTold_ARepeatedReport_IsLoggedAsIgnored()
    {
        HaveApproved().ReportTold(ApprovedAt);
        var logger = new CapturingLogger<TrackCarrierInvoiceToldReportInLoggingScope>();

        await new TrackCarrierInvoiceToldReportInLoggingScope(logger).Handle(ToldReport(), (message, token) => Told.Handle(message, token), CancellationToken.None);

        logger.Messages.Should().ContainSingle().Which.Should().Contain("ignored");
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
    }

    [Fact]
    public async Task TrackTold_AReportForAnUnknownInvoice_RecordsRefusedWithItsFailureCode()
    {
        HaveNoInvoice();
        var logger = new CapturingLogger<TrackCarrierInvoiceToldReportInLoggingScope>();

        await new TrackCarrierInvoiceToldReportInLoggingScope(logger).Handle(ToldReport(number: "VDT-9"), (message, token) => Told.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Refused" && (string?)scope.GetValueOrDefault("failureCode") == "CarrierInvoiceNotFound");
    }

    [Fact]
    public async Task TrackNotDelivered_AReport_IsOnTheTrailOfTheCarrierInvoice_AndLogsTheCause()
    {
        HaveApproved();
        var logger = new CapturingLogger<TrackCarrierInvoiceNotDeliveredReportInLoggingScope>();

        await new TrackCarrierInvoiceNotDeliveredReportInLoggingScope(logger).Handle(NotDeliveredReport(), (message, token) => NotDelivered.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("correlationId") == "corr-1" && (string?)scope.GetValueOrDefault("documentNumber") == "VDT-1");
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("not told").And.Contain("Overloaded");
    }

    [Fact]
    public async Task TrackNotDelivered_ALateReport_IsLoggedAsIgnored()
    {
        HaveApproved().ReportTold(ApprovedAt);
        var logger = new CapturingLogger<TrackCarrierInvoiceNotDeliveredReportInLoggingScope>();

        await new TrackCarrierInvoiceNotDeliveredReportInLoggingScope(logger).Handle(NotDeliveredReport(), (message, token) => NotDelivered.Handle(message, token), CancellationToken.None);

        logger.Messages.Should().ContainSingle().Which.Should().Contain("ignored");
    }

    [Fact]
    public async Task TrackNotDelivered_AReportForAnUnknownInvoice_RecordsRefusedWithItsFailureCode()
    {
        HaveNoInvoice();
        var logger = new CapturingLogger<TrackCarrierInvoiceNotDeliveredReportInLoggingScope>();

        await new TrackCarrierInvoiceNotDeliveredReportInLoggingScope(logger).Handle(NotDeliveredReport(number: "VDT-9"), (message, token) => NotDelivered.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Refused" && (string?)scope.GetValueOrDefault("failureCode") == "CarrierInvoiceNotFound");
    }
}

using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.CreditNotes.ReportNotDelivered;
using Norvia.Erp.Application.CreditNotes.ReportSent;
using Norvia.Erp.Application.CreditNotes.SendAgain;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;
using Norvia.Erp.Application.UnitTests.BillableLines;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.CreditNotes;

/// <summary>A Credit note is reported Sent or Not delivered, and sent again, as an invoice is (E1).</summary>
public sealed class CreditNoteDeliveryHandlerTests
{
    private const string Number = "CN-2026-000001";
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly IRepository<CreditNote> _creditNotes = Substitute.For<IRepository<CreditNote>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly FakeTimeProvider _time = new(IssuedAt.AddMinutes(1));

    private static CreditNote Note()
    {
        var line = BillableLine.Record(
            new BillableLineData(
                "TO-0000001", new ShipperId("SHP-0001"), "PO", "SH-0000001", IssuedAt,
                new FreightCharge(100m, [new ChargeLine(ChargeLineType.Freight, 100m)], "RC-0001", 1), false, "intake", "billing"),
            IssuedAt).Line!;
        var invoice = ShipperInvoice.Issue(InvoiceNumber.Of(2026, 1), new ShipperId("SHP-0001"), [line], IssuedAt, "invoice-corr");
        return CreditNote.Issue(CreditNoteNumber.Of(2026, 1), invoice, "TO-0000001", 40m, "Rate Card error", 0m, IssuedAt, "corr-cn");
    }

    private CreditNote Have(CreditNote? note = null)
    {
        note ??= Note();
        _creditNotes.FirstOrDefaultAsync(Arg.Any<ISpecification<CreditNote>>(), Arg.Any<CancellationToken>()).Returns(note);
        return note;
    }

    private void HaveNone() =>
        _creditNotes.FirstOrDefaultAsync(Arg.Any<ISpecification<CreditNote>>(), Arg.Any<CancellationToken>()).Returns((CreditNote?)null);

    private ReportCreditNoteSentHandler Sent => new(_creditNotes, _time);

    private ReportCreditNoteNotDeliveredHandler NotDelivered => new(_creditNotes, _outbox, _time);

    private SendCreditNoteAgainHandler Again => new(_creditNotes, _shippers, _outbox, _time, NullLogger<SendCreditNoteAgainHandler>.Instance);

    private static ReportCreditNoteNotDeliveredCommand NotDeliveredReport(string number = Number) => new(number, "corr-cn", 503, "Overloaded");

    [Fact]
    public async Task ReportSent_AnIssuedCreditNote_IsSent_AndPublishesNothing()
    {
        var note = Have();

        var result = await Sent.Handle(new ReportCreditNoteSentCommand(Number, "corr-cn"), CancellationToken.None);

        result.Value.Should().Be(new CreditNoteReportResult(Number, "Sent", IssuedAt.AddMinutes(1)));
        note.Status.Should().Be(ShipperInvoiceStatus.Sent);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _creditNotes.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportSent_ACreditNoteReportedAlready_IsIgnored_SavingNothing()
    {
        Have().ReportSent(IssuedAt);

        var result = await Sent.Handle(new ReportCreditNoteSentCommand(Number, "corr-cn"), CancellationToken.None);

        result.Value.Ignored.Should().BeTrue();
        await _creditNotes.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("CN-2026-999999")]
    [InlineData("")]
    public async Task ReportSent_AnUnknownCreditNote_IsRefusedWithCreditNoteNotFound(string number)
    {
        HaveNone();

        var result = await Sent.Handle(new ReportCreditNoteSentCommand(number, "corr-cn"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteNotFound");
    }

    [Fact]
    public async Task ReportNotDelivered_AnIssuedCreditNote_IsNotDelivered_AndPublishesTheEventAsACreditNote()
    {
        var note = Have();

        var result = await NotDelivered.Handle(NotDeliveredReport(), CancellationToken.None);

        result.Value.Should().Be(new CreditNoteReportResult(Number, "NotDelivered", null));
        note.Status.Should().Be(ShipperInvoiceStatus.NotDelivered);
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<ShipperInvoiceNotDeliveredV1>().Should().ContainSingle().Subject;
        published.CorrelationId.Should().Be("corr-cn");
        published.DocumentType.Should().Be("CREDIT_NOTE");
        published.DocumentNumber.Should().Be(Number);
        published.ShipperId.Should().Be("SHP-0001");
        published.StatusCode.Should().Be(503);
        published.Message.Should().Be("Overloaded");
        await _creditNotes.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportNotDelivered_ACreditNoteSentAlready_IsIgnored_PublishingNothing()
    {
        Have().ReportSent(IssuedAt);

        var result = await NotDelivered.Handle(NotDeliveredReport(), CancellationToken.None);

        result.Value.Should().Be(new CreditNoteReportResult(Number, "Sent", IssuedAt, Ignored: true));
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _creditNotes.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportNotDelivered_AnUnknownCreditNote_IsRefusedWithCreditNoteNotFound()
    {
        HaveNone();

        var result = await NotDelivered.Handle(NotDeliveredReport("CN-2026-999999"), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteNotFound");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Track_ASentReport_IsOnTheTrailOfTheCreditNote_AndSucceeds()
    {
        Have();
        var logger = new CapturingLogger<TrackCreditNoteReportInLoggingScope>();

        await new TrackCreditNoteReportInLoggingScope(logger).Handle(
            new ReportCreditNoteSentCommand(Number, "corr-cn"), (message, token) => Sent.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("correlationId") == "corr-cn" && (string?)scope.GetValueOrDefault("documentNumber") == Number);
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("reported Sent");
    }

    [Fact]
    public async Task Track_ARepeatedSentReport_IsLoggedAsIgnored()
    {
        Have().ReportSent(IssuedAt);
        var logger = new CapturingLogger<TrackCreditNoteReportInLoggingScope>();

        await new TrackCreditNoteReportInLoggingScope(logger).Handle(
            new ReportCreditNoteSentCommand(Number, "corr-cn"), (message, token) => Sent.Handle(message, token), CancellationToken.None);

        logger.Messages.Should().ContainSingle().Which.Should().Contain("ignored");
    }

    [Fact]
    public async Task Track_ASentReportForAnUnknownCreditNote_RecordsRefusedWithItsFailureCode()
    {
        HaveNone();
        var logger = new CapturingLogger<TrackCreditNoteReportInLoggingScope>();

        await new TrackCreditNoteReportInLoggingScope(logger).Handle(
            new ReportCreditNoteSentCommand("CN-2026-999999", "corr-cn"), (message, token) => Sent.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Refused" && (string?)scope.GetValueOrDefault("failureCode") == "CreditNoteNotFound");
    }

    [Fact]
    public async Task Track_ANotDeliveredReport_IsOnTheTrailOfTheCreditNote_AndSucceeds()
    {
        Have();
        var logger = new CapturingLogger<TrackCreditNoteNotDeliveredReportInLoggingScope>();

        await new TrackCreditNoteNotDeliveredReportInLoggingScope(logger).Handle(NotDeliveredReport(), (message, token) => NotDelivered.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("correlationId") == "corr-cn" && (string?)scope.GetValueOrDefault("documentNumber") == Number);
        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("Not delivered").And.Contain("Overloaded");
    }

    [Fact]
    public async Task Track_ALateNotDeliveredReport_IsLoggedAsIgnored()
    {
        Have().ReportSent(IssuedAt);
        var logger = new CapturingLogger<TrackCreditNoteNotDeliveredReportInLoggingScope>();

        await new TrackCreditNoteNotDeliveredReportInLoggingScope(logger).Handle(NotDeliveredReport(), (message, token) => NotDelivered.Handle(message, token), CancellationToken.None);

        logger.Messages.Should().ContainSingle().Which.Should().Contain("is Sent").And.Contain("ignored");
    }

    [Fact]
    public async Task Track_ANotDeliveredReportForAnUnknownCreditNote_RecordsRefusedWithItsFailureCode()
    {
        HaveNone();
        var logger = new CapturingLogger<TrackCreditNoteNotDeliveredReportInLoggingScope>();

        await new TrackCreditNoteNotDeliveredReportInLoggingScope(logger).Handle(
            NotDeliveredReport("CN-2026-999999"), (message, token) => NotDelivered.Handle(message, token), CancellationToken.None);

        logger.Scopes.Should().Contain(scope => (string?)scope.GetValueOrDefault("outcome") == "Refused" && (string?)scope.GetValueOrDefault("failureCode") == "CreditNoteNotFound");
    }

    [Fact]
    public async Task SendAgain_ANotDeliveredCreditNote_IsIssuedOnceMore_AndPublishesTheSameCreditNoteAgain()
    {
        var note = Have();
        note.ReportNotDelivered(500, "cause", IssuedAt.AddMinutes(1));
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));

        var result = await Again.Handle(new SendCreditNoteAgainCommand(Number), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        note.Status.Should().Be(ShipperInvoiceStatus.Issued);
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<CreditNoteIssuedV1>().Should().ContainSingle().Subject;
        published.CorrelationId.Should().Be("corr-cn");
        published.CreditNote.CreditNoteNumber.Should().Be(Number);
        published.CreditNote.Shipper.Name.Should().Be("Brouwer Dranken");
        published.CreditNote.Status.Should().Be("Issued");
        published.CreditNote.NotDelivered.Should().BeNull();
        await _creditNotes.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAgain_ACreditNoteThatIsNotNotDelivered_IsRefused_PublishingNothing(bool sent)
    {
        var note = Have();
        if (sent)
        {
            note.ReportSent(IssuedAt);
        }

        var result = await Again.Handle(new SendCreditNoteAgainCommand(Number), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteNotSendableAgain");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _creditNotes.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("CN-2026-999999")]
    [InlineData("")]
    public async Task SendAgain_AnUnknownCreditNote_IsRefusedWithCreditNoteNotFound(string number)
    {
        HaveNone();

        var result = await Again.Handle(new SendCreditNoteAgainCommand(number), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteNotFound");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task View_ACreditNoteListedAfterItWasReportedNotDelivered_CarriesTheCause()
    {
        var note = Note();
        note.ReportNotDelivered(null, "timed out", IssuedAt.AddMinutes(1));
        var creditNotes = Substitute.For<IReadRepository<CreditNote>>();
        creditNotes.ListAsync(Arg.Any<ISpecification<CreditNote>>(), Arg.Any<CancellationToken>()).Returns([note]);
        _shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns([]);

        var views = await new Norvia.Erp.Application.CreditNotes.List.ListCreditNotesHandler(creditNotes, _shippers)
            .Handle(new Norvia.Erp.Application.CreditNotes.List.ListCreditNotesQuery(), CancellationToken.None);

        views.Single().Status.Should().Be("NotDelivered");
        views.Single().NotDelivered.Should().Be(new NotDeliveredView(IssuedAt.AddMinutes(1), null, "timed out"));
    }
}

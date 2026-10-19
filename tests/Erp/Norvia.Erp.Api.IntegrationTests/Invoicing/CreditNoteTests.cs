using Ardalis.Result;
using Norvia.Erp.Application.CreditNotes.Issue;

namespace Norvia.Erp.Api.IntegrationTests.Invoicing;

/// <summary>Finance's <b>Credit note</b> on an invoice line (story 5 and 13): full or partial, with a reason, never above what is left on the line, with gapless yearly numbers.</summary>
public sealed class CreditNoteTests(ErpFixture fixture) : ErpTest(fixture)
{
    private const string Reason = "The pallets were delivered damaged.";

    private async Task<string> InvoiceWithTwoLinesAsync()
    {
        // TO-0000401 is 150.00 and TO-0000402 is 100.00.
        await RecordAsync(Billable("TO-0000401", freight: 96.40m, minimumChargeAdjustment: 53.60m));
        await RecordAsync(Billable("TO-0000402", freight: 100m));
        return (await InvoiceAsync(Brouwer)).InvoiceNumber;
    }

    [Fact]
    public async Task CreditNote_ForTheWholeLine_IsIssuedWithTheNextNumberVatAndReason_AndTheInvoiceIsNotChanged()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();
        var before = await OpenInvoiceAsync(invoiceNumber);

        var result = await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 150m, Reason, Ct);

        result.Number.Should().Be("CN-2026-000001");
        var note = (await CreditNotesAsync(invoiceNumber)).Should().ContainSingle().Subject;
        note.Should().BeEquivalentTo(new
        {
            CreditNoteNumber = "CN-2026-000001",
            CreditNoteDate = new DateOnly(2026, 10, 19),
            InvoiceNumber = invoiceNumber,
            TransportOrderNumber = "TO-0000401",
            ShipperReference = "REF-TO-0000401",
            Currency = "EUR",
            TotalWithoutVat = 150m,
            Vat = 31.50m,
            TotalWithVat = 181.50m,
            Reason,
            Status = "Issued",
        });
        (await OpenInvoiceAsync(invoiceNumber)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task CreditNote_AFewPartialOnesOnOneLine_AreIssuedUntilTheLineTotalIsUsedUp_AndNoFurther()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();

        (await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 60m, Reason, Ct)).Number.Should().Be("CN-2026-000001");
        (await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 89.99m, Reason, Ct)).Number.Should().Be("CN-2026-000002");
        var beyond = await Finance.SendAsync(new IssueCreditNoteCommand(invoiceNumber, "TO-0000401", 0.02m, Reason), Ct);
        var exactlyTheRest = await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 0.01m, Reason, Ct);

        beyond.Status.Should().Be(ResultStatus.Invalid);
        beyond.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteAboveLineTotal");
        exactlyTheRest.Number.Should().Be("CN-2026-000003");
        (await CreditNotesAsync(invoiceNumber)).Should().HaveCount(3);
    }

    [Fact]
    public async Task CreditNote_AboveTheLineTotal_IsRefusedWithCreditNoteAboveLineTotal_AndPublishesNothing()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();

        var result = await Finance.SendAsync(new IssueCreditNoteCommand(invoiceNumber, "TO-0000402", 100.01m, Reason), Ct);
        var shown = await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000402", 100.01m, Reason, Ct);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteAboveLineTotal");
        shown.Refused.Should().BeTrue();
        shown.Refusal.Should().NotBeNullOrWhiteSpace();
        (await CreditNotesAsync()).Should().BeEmpty();
        (await PublishedAsync("CreditNoteIssued")).Should().BeEmpty();
    }

    [Fact]
    public async Task CreditNote_EachLineHasItsOwnRemainder()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();
        await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 150m, Reason, Ct);

        var otherLine = await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000402", 100m, Reason, Ct);

        otherLine.Number.Should().Be("CN-2026-000002");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreditNote_WithoutAReason_IsRefusedWithCreditNoteReasonRequired(string? reason)
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();

        var result = await Finance.SendAsync(new IssueCreditNoteCommand(invoiceNumber, "TO-0000401", 10m, reason), Ct);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteReasonRequired");
        (await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 10m, reason, Ct)).Refused.Should().BeTrue();
        (await CreditNotesAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public async Task CreditNote_ForAnAmountThatIsNotAPositiveAmountInCents_IsRefusedWithCreditNoteAmountInvalid(double amount)
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();

        var result = await Finance.SendAsync(new IssueCreditNoteCommand(invoiceNumber, "TO-0000401", (decimal)amount, Reason), Ct);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CreditNoteAmountInvalid");
        (await CreditNotesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CreditNote_OnAnInvoiceOrLineTheErpDoesNotHave_IsRefusedWithTheCodeThatSaysWhich()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();

        var noInvoice = await Finance.SendAsync(new IssueCreditNoteCommand("INV-2026-999999", "TO-0000401", 10m, Reason), Ct);
        var noLine = await Finance.SendAsync(new IssueCreditNoteCommand(invoiceNumber, "TO-0000999", 10m, Reason), Ct);

        noInvoice.ValidationErrors.Single().ErrorCode.Should().Be("ShipperInvoiceNotFound");
        noLine.ValidationErrors.Single().ErrorCode.Should().Be("InvoiceLineNotFound");
        (await CreditNotesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CreditNote_ARefusedOne_TakesNoNumber()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();
        await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000402", 500m, Reason, Ct);

        var issued = await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000402", 40m, Reason, Ct);

        issued.Number.Should().Be("CN-2026-000001");
    }

    [Fact]
    public async Task CreditNote_Numbers_AreASequenceOfTheirOwnThatRestartsEachYear()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();
        await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 10m, Reason, Ct);
        Now(new DateTimeOffset(2027, 2, 2, 9, 0, 0, TimeSpan.Zero));

        var inTheNewYear = await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 10m, Reason, Ct);

        inTheNewYear.Number.Should().Be("CN-2027-000001");
        (await CreditNotesAsync(invoiceNumber)).Select(note => note.CreditNoteNumber).Should().Equal("CN-2027-000001", "CN-2026-000001");
    }

    [Fact]
    public async Task CreditNote_PublishesCreditNoteIssuedOnce_UnderACorrelationIdOfItsOwn()
    {
        var invoiceNumber = await InvoiceWithTwoLinesAsync();
        var invoice = await OpenInvoiceAsync(invoiceNumber);

        await Finance.IssueCreditNoteAsync(invoiceNumber, "TO-0000401", 60m, Reason, Ct);

        var note = (await CreditNotesAsync(invoiceNumber)).Single();
        var message = (await PublishedAsync("CreditNoteIssued")).Should().ContainSingle().Subject;
        message.Topic.Should().Be("invoicing-events");
        message.CorrelationId.Should().Be(note.CorrelationId);
        message.Body["correlationId"]!.GetValue<string>().Should().Be(note.CorrelationId);
        message.Body["creditNote"]!["creditNoteNumber"]!.GetValue<string>().Should().Be("CN-2026-000001");
        message.Body["creditNote"]!["invoiceNumber"]!.GetValue<string>().Should().Be(invoiceNumber);
        message.Body["creditNote"]!["totalWithoutVat"]!.GetValue<decimal>().Should().Be(60m);
        message.Properties["eventType"].Should().Be("CreditNoteIssued");
        message.Properties["correlationId"].Should().Be(note.CorrelationId);
        message.Properties["partyId"].Should().Be(Brouwer);
        message.DocumentNumber.Should().Be("CN-2026-000001");
        note.CorrelationId.Should().NotBe(invoice.CorrelationId);
    }
}

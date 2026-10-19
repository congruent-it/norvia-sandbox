using Norvia.Erp.Application.ShipperInvoices;

namespace Norvia.Erp.Api.IntegrationTests.Invoicing;

/// <summary>Finance's <b>Invoice now</b> (story 3, 4, 10 and 13): one Shipper invoice per Shipper over its Waiting lines, with gapless numbers and VAT, and no line invoiced twice.</summary>
public sealed class InvoiceNowTests(ErpFixture fixture) : ErpTest(fixture)
{
    [Fact]
    public async Task InvoiceNow_AShippersWaitingLines_IssuesOneInvoiceWithOneLinePerTransportOrder_AndTheOtherShippersLineStaysWaiting()
    {
        await RecordAsync(Billable("TO-0000201", Brouwer, freight: 412.50m));
        await RecordAsync(Billable("TO-0000202", Brouwer, freight: 96.40m, minimumChargeAdjustment: 53.60m));
        await RecordAsync(Billable("TO-0000203", Groenrijk, freight: 80m));

        var result = await Finance.InvoiceNowAsync(Brouwer, Ct);

        result.Number.Should().Be("INV-2026-000001");
        result.LineCount.Should().Be(2);
        var invoice = await OpenInvoiceAsync("INV-2026-000001");
        invoice.Status.Should().Be("Issued");
        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 10, 19));
        invoice.Shipper.Should().Be(new ShipperOnInvoiceView(Brouwer, "Brouwer Dranken"));
        invoice.Lines.Select(line => line.TransportOrderNumber).Should().BeEquivalentTo(["TO-0000201", "TO-0000202"]);
        invoice.Lines.Single(line => line.TransportOrderNumber == "TO-0000202").ChargeLines
            .Select(charge => (charge.Type, charge.Amount)).Should().Equal(("Freight", 96.40m), ("MinimumChargeAdjustment", 53.60m));
        invoice.Lines.Single(line => line.TransportOrderNumber == "TO-0000201").ShipperReference.Should().Be("REF-TO-0000201");
        (await WaitingLinesOfAsync(Brouwer)).Should().BeEmpty();
        (await WaitingLinesOfAsync(Groenrijk)).Should().ContainSingle().Which.TransportOrderNumber.Should().Be("TO-0000203");
    }

    [Fact]
    public async Task InvoiceNow_Totals_AreTheLinesPlus21PercentVatRoundedToCentsHalfAwayFromZero()
    {
        // 412.50 + 150.00 = 562.50, and 21% of it is 118.125, which rounds up to 118.13.
        await RecordAsync(Billable("TO-0000211", freight: 412.50m));
        await RecordAsync(Billable("TO-0000212", freight: 150m));

        var invoice = await InvoiceAsync(Brouwer);

        invoice.Currency.Should().Be("EUR");
        invoice.TotalWithoutVat.Should().Be(562.50m);
        invoice.Vat.Should().Be(118.13m);
        invoice.TotalWithVat.Should().Be(680.63m);
        invoice.Lines.Select(line => line.Total).Should().BeEquivalentTo([412.50m, 150m]);
    }

    [Fact]
    public async Task InvoiceNow_ASecondRunStraightAfter_InvoicesNothingTwice_AndPublishesNothingMore()
    {
        await RecordAsync(Brouwer, "TO-0000221", "TO-0000222");
        await InvoiceAsync(Brouwer);

        var second = await Finance.InvoiceNowAsync(Brouwer, Ct);

        second.Should().Be(new ActionResult());
        (await InvoicesAsync()).Should().ContainSingle();
        (await PublishedAsync("ShipperInvoiceIssued")).Should().ContainSingle();
    }

    [Fact]
    public async Task InvoiceNow_AShipperWithoutWaitingLines_GetsNoInvoice_AndTakesNoNumber()
    {
        await RecordAsync(Brouwer, "TO-0000231");

        var none = await Finance.InvoiceNowAsync(Hoeve, Ct);
        var next = await InvoiceAsync(Brouwer);

        none.Should().Be(new ActionResult());
        next.InvoiceNumber.Should().Be("INV-2026-000001");
        (await InvoicesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task InvoiceNow_TwoShippers_GetTheNextGaplessNumbers_AndEachInvoiceHoldsOnlyItsShippersLines()
    {
        await RecordAsync(Brouwer, "TO-0000241", "TO-0000242");
        await RecordAsync(Groenrijk, "TO-0000243");

        var first = await InvoiceAsync(Brouwer);
        var second = await InvoiceAsync(Groenrijk);

        first.InvoiceNumber.Should().Be("INV-2026-000001");
        second.InvoiceNumber.Should().Be("INV-2026-000002");
        first.Lines.Select(line => line.TransportOrderNumber).Should().BeEquivalentTo(["TO-0000241", "TO-0000242"]);
        second.Lines.Select(line => line.TransportOrderNumber).Should().BeEquivalentTo(["TO-0000243"]);
        (await InvoicesAsync()).Select(invoice => invoice.InvoiceNumber).Should().Equal("INV-2026-000002", "INV-2026-000001");
    }

    [Fact]
    public async Task InvoiceNow_LinesRecordedAfterAnInvoice_GoOnTheNextInvoice()
    {
        await RecordAsync(Brouwer, "TO-0000251");
        await InvoiceAsync(Brouwer);
        await RecordAsync(Brouwer, "TO-0000252");

        var next = await InvoiceAsync(Brouwer);

        next.InvoiceNumber.Should().Be("INV-2026-000002");
        next.Lines.Should().ContainSingle().Which.TransportOrderNumber.Should().Be("TO-0000252");
    }

    [Fact]
    public async Task InvoiceNow_AShipperTheErpDoesNotKnow_IsRefused_AndIssuesNothing()
    {
        var result = await Finance.InvoiceNowAsync("SHP-9999", Ct);

        result.Refused.Should().BeTrue();
        result.Refusal.Should().Contain("SHP-9999");
        (await InvoicesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task InvoiceNow_AnInvoiceInANewYear_StartsTheYearsNumbersAtOne()
    {
        await RecordAsync(Brouwer, "TO-0000261");
        await InvoiceAsync(Brouwer);
        Now(new DateTimeOffset(2027, 1, 5, 9, 0, 0, TimeSpan.Zero));
        await RecordAsync(Brouwer, "TO-0000262");

        var invoice = await InvoiceAsync(Brouwer);

        invoice.InvoiceNumber.Should().Be("INV-2027-000001");
        (await InvoicesAsync()).Select(shown => shown.InvoiceNumber).Should().Equal("INV-2027-000001", "INV-2026-000001");
    }

    [Fact]
    public async Task InvoiceNow_JustAfterMidnightInAmsterdamOnNewYearsDay_IsDatedAndNumberedInTheNewYear()
    {
        // 23:30 UTC on 31 December is 00:30 on 1 January in Amsterdam.
        Now(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero));
        await RecordAsync(Brouwer, "TO-0000271");

        var invoice = await InvoiceAsync(Brouwer);

        invoice.InvoiceNumber.Should().Be("INV-2027-000001");
        invoice.InvoiceDate.Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public async Task InvoiceNow_TwoRunsAtOnce_InvoiceEachLineOnce()
    {
        await RecordAsync(Brouwer, "TO-0000281", "TO-0000282", "TO-0000283");

        var results = await Task.WhenAll(Finance.InvoiceNowAsync(Brouwer, Ct), Finance.InvoiceNowAsync(Brouwer, Ct));

        results.Where(result => result.Number is not null).Should().ContainSingle();
        results.Should().OnlyContain(result => !result.Refused);
        var invoices = await InvoicesAsync();
        invoices.Should().ContainSingle().Which.Lines.Should().HaveCount(3);
        (await PublishedAsync("ShipperInvoiceIssued")).Should().ContainSingle();
    }

    [Fact]
    public async Task InvoiceNow_PublishesShipperInvoiceIssuedOnce_UnderTheInvoicesOwnCorrelationId()
    {
        var recorded = await RecordAsync(Billable("TO-0000291"));
        var invoice = await InvoiceAsync(Brouwer);

        var message = (await PublishedAsync("ShipperInvoiceIssued")).Should().ContainSingle().Subject;

        message.Topic.Should().Be("invoicing-events");
        message.CorrelationId.Should().Be(invoice.CorrelationId);
        message.Body["correlationId"]!.GetValue<string>().Should().Be(invoice.CorrelationId);
        message.Body["invoice"]!["correlationId"]!.GetValue<string>().Should().Be(invoice.CorrelationId);
        message.Body["invoice"]!["invoiceNumber"]!.GetValue<string>().Should().Be(invoice.InvoiceNumber);
        message.Body["invoice"]!["lines"]!.AsArray().Should().ContainSingle();
        message.Body["eventId"]!.GetValue<string>().Should().Be(message.MessageId);
        message.Properties["eventType"].Should().Be("ShipperInvoiceIssued");
        message.Properties["correlationId"].Should().Be(invoice.CorrelationId);
        message.Properties["partyId"].Should().Be(Brouwer);
        message.DocumentNumber.Should().Be(invoice.InvoiceNumber);

        // The invoice starts a trail of its own: the correlation IDs of the intake and of the billing stay on the line.
        var line = recorded["line"]!;
        invoice.CorrelationId.Should().NotBe(line["billingCorrelationId"]!.GetValue<string>());
        invoice.CorrelationId.Should().NotBe(line["intakeCorrelationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvoiceNow_AShipperWithoutLines_PublishesNothing()
    {
        await Finance.InvoiceNowAsync(Brouwer, Ct);

        (await Fixture.PublishedSettledAsync(Ct)).Should().BeEmpty();
    }
}

namespace Norvia.Erp.Api.IntegrationTests.Invoicing;

/// <summary>
/// The monthly Invoice run (story 2): on the 1st at 06:00 Europe/Amsterdam, over the lines delivered before the 1st. The tests set the ERP's clock; each
/// looks as a freshly started ERP does, so none depends on the order of the others. Amsterdam is UTC+1 from 25 October 2026, so 06:00 on 1 November is 05:00 UTC.
/// </summary>
public sealed class MonthlyInvoiceRunTests(ErpFixture fixture) : ErpTest(fixture)
{
    private static readonly DateTimeOffset FirstOfNovemberAt0600 = new(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DeliveredLateOnThe31st = new(2026, 10, 31, 23, 30, 0, TimeSpan.FromHours(1));

    private static readonly DateTimeOffset DeliveredAfterMidnightOnThe1st = new(2026, 11, 1, 0, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public async Task MonthlyRun_OnThe1stAt0600_InvoicesTheLinesDeliveredBeforeThe1st_AndLeavesTheOthersWaiting()
    {
        await RecordAsync(Billable("TO-0000301", deliveryTime: DeliveredLateOnThe31st));
        await RecordAsync(Billable("TO-0000302", deliveryTime: DeliveredAfterMidnightOnThe1st));
        await RecordAsync(Billable("TO-0000303", deliveryTime: new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(2))));

        await Finance.RunMonthlyAt(FirstOfNovemberAt0600, Ct);

        var invoice = (await InvoicesAsync()).Should().ContainSingle().Subject;
        invoice.Lines.Select(line => line.TransportOrderNumber).Should().BeEquivalentTo(["TO-0000301", "TO-0000303"]);
        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 11, 1));
        (await WaitingLinesOfAsync(Brouwer)).Should().ContainSingle().Which.TransportOrderNumber.Should().Be("TO-0000302");
    }

    [Fact]
    public async Task MonthlyRun_BeforeTheRunTimeOnThe1st_DoesNotYetTakeTheLinesOfTheMonthThatJustEnded()
    {
        await RecordAsync(Billable("TO-0000311", deliveryTime: new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(2))));
        await RecordAsync(Billable("TO-0000312", deliveryTime: DeliveredLateOnThe31st));

        // 05:59 in Amsterdam: the latest run due is the one of 1 October.
        await Finance.RunMonthlyAt(FirstOfNovemberAt0600.AddMinutes(-1), Ct);

        var invoice = (await InvoicesAsync()).Should().ContainSingle().Subject;
        invoice.Lines.Select(line => line.TransportOrderNumber).Should().Equal("TO-0000311");
        (await WaitingLinesOfAsync(Brouwer)).Should().ContainSingle().Which.TransportOrderNumber.Should().Be("TO-0000312");
    }

    [Fact]
    public async Task MonthlyRun_AShipperWithoutLinesBeforeThe1st_GetsNoInvoice_AndEachOtherShipperGetsOneOfItsOwn()
    {
        await RecordAsync(Billable("TO-0000321", Brouwer, deliveryTime: DeliveredLateOnThe31st));
        await RecordAsync(Billable("TO-0000322", Brouwer, deliveryTime: DeliveredLateOnThe31st));
        await RecordAsync(Billable("TO-0000323", Groenrijk, deliveryTime: DeliveredLateOnThe31st));
        await RecordAsync(Billable("TO-0000324", Hoeve, deliveryTime: DeliveredAfterMidnightOnThe1st));

        await Finance.RunMonthlyAt(FirstOfNovemberAt0600, Ct);

        var invoices = await InvoicesAsync();
        invoices.Should().HaveCount(2);
        invoices.Select(invoice => (invoice.Shipper.ShipperId, invoice.Lines.Count)).Should().BeEquivalentTo([(Brouwer, 2), (Groenrijk, 1)]);
        invoices.Select(invoice => invoice.InvoiceNumber).Should().BeEquivalentTo(["INV-2026-000001", "INV-2026-000002"]);
        (await PublishedAsync("ShipperInvoiceIssued")).Should().HaveCount(2);
    }

    [Fact]
    public async Task MonthlyRun_RunAgain_InvoicesNothingTwice()
    {
        await RecordAsync(Billable("TO-0000331", deliveryTime: DeliveredLateOnThe31st));
        await Finance.RunMonthlyAt(FirstOfNovemberAt0600, Ct);

        await Finance.RunMonthlyAt(FirstOfNovemberAt0600.AddMinutes(1), Ct);
        await Finance.RunMonthlyAt(FirstOfNovemberAt0600.AddDays(3), Ct);

        (await InvoicesAsync()).Should().ContainSingle();
        (await PublishedAsync("ShipperInvoiceIssued")).Should().ContainSingle();
    }

    [Fact]
    public async Task MonthlyRun_AfterInvoiceNowForAShipper_InvoicesOnlyTheLinesNotInvoicedYet()
    {
        await RecordAsync(Billable("TO-0000341", Brouwer, deliveryTime: DeliveredLateOnThe31st));
        await RecordAsync(Billable("TO-0000342", Groenrijk, deliveryTime: DeliveredLateOnThe31st));
        var byHand = await InvoiceAsync(Brouwer);

        await Finance.RunMonthlyAt(FirstOfNovemberAt0600, Ct);

        var invoices = await InvoicesAsync();
        invoices.Should().HaveCount(2);
        invoices.Single(invoice => invoice.Shipper.ShipperId == Brouwer).InvoiceNumber.Should().Be(byHand.InvoiceNumber);
        invoices.Single(invoice => invoice.Shipper.ShipperId == Groenrijk).InvoiceNumber.Should().Be("INV-2026-000002");
    }

    [Fact]
    public async Task MonthlyRun_OnThe1stOfJanuary_NumbersTheInvoicesOfTheNewYearFromOne()
    {
        await RecordAsync(Billable("TO-0000351", deliveryTime: new DateTimeOffset(2026, 12, 31, 20, 0, 0, TimeSpan.FromHours(1))));

        // 06:00 in Amsterdam on 1 January 2027 is 05:00 UTC.
        await Finance.RunMonthlyAt(new DateTimeOffset(2027, 1, 1, 5, 0, 0, TimeSpan.Zero), Ct);

        (await InvoicesAsync()).Should().ContainSingle().Which.InvoiceNumber.Should().Be("INV-2027-000001");
    }

    [Fact]
    public async Task MonthlyRun_OverNothingWaiting_ChangesAndPublishesNothing()
    {
        await Finance.RunMonthlyAt(FirstOfNovemberAt0600, Ct);

        (await InvoicesAsync()).Should().BeEmpty();
        (await Fixture.PublishedSettledAsync(Ct)).Should().BeEmpty();
    }
}

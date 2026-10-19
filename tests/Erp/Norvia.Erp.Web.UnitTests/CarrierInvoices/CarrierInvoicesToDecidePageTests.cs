using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.List;
using Norvia.Erp.Web.CarrierInvoices;
using Norvia.Erp.Web.Live;
using Norvia.Erp.Web.Operators;
using static Norvia.Erp.Web.UnitTests.CarrierInvoices.CarrierInvoicesToDecideTestData;

namespace Norvia.Erp.Web.UnitTests.CarrierInvoices;

public sealed class CarrierInvoicesToDecidePageTests : ErpWebContext
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    public CarrierInvoicesToDecidePageTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = Refresh }));
    }

    private void Have(params CarrierInvoiceToDecide[] items) => Answer<ListCarrierInvoicesToDecideQuery, IReadOnlyList<CarrierInvoiceToDecide>>(items);

    [Fact]
    public void Render_ShowsEachInvoiceWithItsNumberOfLinesPerResult_InTheOrderAnswered()
    {
        Have(
            Item("VDT-1", Cost(), Line("TRX-1", "PriceMismatch"), Line("TRX-2", "UnknownBooking"), Line("TRX-3", "Matched", "Matched")),
            Item("VDT-2", null, Line("TRX-9", "UnknownBooking")));

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-1"));
        page.FindAll("section h2").Select(heading => heading.TextContent.Trim()).Should().Equal("VDT-1 · Van Dijk Transport", "VDT-2 · Van Dijk Transport");
        page.Find("section[aria-label='Carrier invoice VDT-1 of Van Dijk Transport'] p").TextContent
            .Should().Contain("2026-10-19").And.Contain("€ 240.00").And.Contain("1 Price mismatch, 1 Unknown booking, 1 Matched");
    }

    [Fact]
    public void Render_ALineWithAnOpenCarrierCost_ShowsTheCostBesideItAndOffersAccept()
    {
        Have(Item("VDT-1", Cost(), Line("TRX-1", "PriceMismatch")));

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Spot price. The Planner's remark: Urgent load"));
        page.Markup.Should().Contain("Freight").And.Contain("€ 70.00").And.Contain("Carrier cost");
        page.Find("fluent-button[aria-label='Accept TRX-1']").Should().NotBeNull();
        page.Find("fluent-button[aria-label='Reject TRX-1']").Should().NotBeNull();
    }

    [Fact]
    public void Render_ALineWithoutACarrierCost_OffersRejectOnly()
    {
        Have(Item("VDT-1", null, Line("TRX-2", "UnknownBooking")));

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("No Carrier cost has arrived for this line."));
        page.FindAll("fluent-button[aria-label='Accept TRX-2']").Should().BeEmpty();
        page.Find("fluent-button[aria-label='Reject TRX-2']").Should().NotBeNull();
    }

    [Fact]
    public void Render_ADuplicateLine_WhoseCostIsInvoiced_OffersRejectOnly()
    {
        Have(Item("VDT-1", Cost("Invoiced", spot: false), Line("TRX-1", "Duplicate")));

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Carrier Rate Card CRC-0001 version 1"));
        page.FindAll("fluent-button[aria-label='Accept TRX-1']").Should().BeEmpty();
        page.Find("fluent-button[aria-label='Reject TRX-1']").Should().NotBeNull();
    }

    [Fact]
    public void Render_ADecidedLine_ShowsItsStatusAndRemarkAndOffersNoDecision()
    {
        Have(Item("VDT-1", Cost(), Line("TRX-1", "PriceMismatch", "Accepted", remark: "Agreed rate"), Line("TRX-2", "UnknownBooking")));

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Agreed rate"));
        page.Find("dl[aria-label='Line TRX-1']").TextContent.Should().Contain("Accepted").And.Contain("Remark");
        page.FindAll("fluent-button[aria-label='Reject TRX-1']").Should().BeEmpty();
        page.Find("fluent-button[aria-label='Reject TRX-2']").Should().NotBeNull();
    }

    [Fact]
    public void Render_ARejectedLine_LabelsItsRemarkAsTheReason()
    {
        Have(Item("VDT-1", null, Line("TRX-2", "UnknownBooking", "Rejected", remark: "Not ours")));

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Find("dl[aria-label='Line TRX-2']").TextContent.Should().Contain("Reason").And.Contain("Not ours"));
    }

    [Fact]
    public void Render_WithNone_SaysNothingWaits()
    {
        Have();

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("No Carrier invoice waits for a decision."));
    }

    [Fact]
    public void Render_WhenTheFirstReadFails_SaysSo()
    {
        Fail<ListCarrierInvoicesToDecideQuery, IReadOnlyList<CarrierInvoiceToDecide>>(new InvalidOperationException("database down"));

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("couldn't be loaded"));
    }

    [Fact]
    public void Render_WhenAnInvoiceArrivesMeanwhile_ListsItWithoutARefresh()
    {
        Have(Item("VDT-1", null, Line("TRX-2", "UnknownBooking")));
        var page = Render<CarrierInvoicesToDecidePage>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-1"));

        Have(Item("VDT-1", null, Line("TRX-2", "UnknownBooking")), Item("VDT-2", null, Line("TRX-9", "UnknownBooking")));
        _time.Advance(Refresh);

        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-2"));
    }

    [Fact]
    public void Page_IsForFinanceOnly() =>
        typeof(CarrierInvoicesToDecidePage).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == FinanceSignIn.FinancePolicy);
}

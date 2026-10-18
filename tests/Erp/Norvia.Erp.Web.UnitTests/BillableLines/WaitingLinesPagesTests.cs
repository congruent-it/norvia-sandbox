using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.BillableLines.ListWaiting;
using Norvia.Erp.Application.BillableLines.ListWaitingOfShipper;
using Norvia.Erp.Web.BillableLines;
using Norvia.Erp.Web.Live;

namespace Norvia.Erp.Web.UnitTests.BillableLines;

public sealed class WaitingLinesPagesTests : ErpWebContext
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    public WaitingLinesPagesTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = Refresh }));
    }

    private static BillableLineView Line(string number, decimal total, bool withoutProof = false) => new(
        number,
        "SHP-0001",
        "PO-" + number,
        "SH-0000123",
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
        new FreightChargeView("EUR", total, [new ChargeLineView("Freight", total - 5m), new ChargeLineView("MinimumChargeAdjustment", 5m)], "RC-0001", 3),
        withoutProof,
        "intake-1",
        "billing-1",
        new DateTimeOffset(2026, 10, 18, 11, 0, 0, TimeSpan.Zero),
        "Waiting");

    [Fact]
    public void RenderWaiting_ShowsEachShippersCountAndTotal_LinkingToItsLines()
    {
        Answer<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>([new WaitingShipperView("SHP-0001", "Brouwer Dranken", 2, 1234.5m)]);

        var page = Render<WaitingLinesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Brouwer Dranken").And.Contain("€ 1,234.50"));
        page.FindAll("td").Select(cell => cell.TextContent.Trim()).Should().Contain(["2", "SHP-0001"]);
        page.Find("a[href='/billable-lines/SHP-0001']").TextContent.Should().Be("Brouwer Dranken");
    }

    [Fact]
    public void RenderWaiting_WithNone_SaysNothingIsWaiting()
    {
        Answer<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>([]);

        var page = Render<WaitingLinesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("No billable lines are waiting"));
    }

    [Fact]
    public void RenderWaiting_WhileReading_ShowsItIsLoading()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<WaitingShipperView>>();
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<ListWaitingBillableLinesQuery>(), Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<IReadOnlyList<WaitingShipperView>>(pending.Task));
#pragma warning restore CA2012

        var page = Render<WaitingLinesPage>();

        page.Markup.Should().Contain("Loading the Waiting billable lines");
        pending.SetResult([]);
    }

    [Fact]
    public void RenderWaiting_WhenTheFirstReadFails_SaysSoAndKeepsTrying()
    {
        Fail<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>(new InvalidOperationException("database down"));

        var page = Render<WaitingLinesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("couldn't be loaded"));
    }

    [Fact]
    public void RenderWaiting_WhenALineArrivesMeanwhile_ListsItWithoutARefresh()
    {
        Answer<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>([new WaitingShipperView("SHP-0001", "Brouwer Dranken", 1, 10m)]);
        var page = Render<WaitingLinesPage>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("€ 10.00"));

        Answer<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>([new WaitingShipperView("SHP-0001", "Brouwer Dranken", 2, 25m)]);
        _time.Advance(Refresh);

        page.WaitForAssertion(() => page.Markup.Should().Contain("€ 25.00"));
    }

    [Fact]
    public void RenderWaiting_WhenARefreshFails_KeepsTheListAndWarns()
    {
        Answer<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>([new WaitingShipperView("SHP-0001", "Brouwer Dranken", 1, 10m)]);
        var page = Render<WaitingLinesPage>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Brouwer Dranken"));

        Fail<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>(new InvalidOperationException("database down"));
        _time.Advance(Refresh);

        page.WaitForAssertion(() => page.Markup.Should().Contain("couldn't be refreshed").And.Contain("Brouwer Dranken"));
    }

    [Fact]
    public void RenderShipper_ShowsEachLineOnOpening_AndMarksAReleaseWithoutProofOfDelivery()
    {
        Answer<ListWaitingBillableLinesOfShipperQuery, WaitingShipperLinesView?>(
            new WaitingShipperLinesView("SHP-0001", "Brouwer Dranken", 40m, [Line("TO-0000001", 15m), Line("TO-0000002", 25m, withoutProof: true)]));

        var page = Render<WaitingLinesOfShipperPage>(parameters => parameters.Add(p => p.ShipperId, "SHP-0001"));

        page.WaitForAssertion(() => page.Markup.Should().Contain("Waiting billable lines of Brouwer Dranken"));
        page.Markup.Should().Contain("2 Waiting lines, € 40.00 without VAT.");
        page.Markup.Should().Contain("TO-0000001 · € 15.00").And.Contain("TO-0000002 · € 25.00 · Released without Proof of delivery");
        var details = page.FindComponents<BillableLineDetails>();
        details.Should().HaveCount(2);
        details[0].Markup.Should().Contain("PO-TO-0000001").And.Contain("SH-0000123").And.Contain("2026-10-17 14:30 +02:00")
            .And.Contain("RC-0001 version 3").And.Contain("intake-1").And.Contain("billing-1")
            .And.Contain("MinimumChargeAdjustment").And.Contain("€ 5.00").And.NotContain("Released without Proof of delivery.");
        details[1].Markup.Should().Contain("Released without Proof of delivery.");
    }

    [Fact]
    public void RenderShipper_AnUnknownShipper_SaysTheErpHasNone()
    {
        Answer<ListWaitingBillableLinesOfShipperQuery, WaitingShipperLinesView?>(null);

        var page = Render<WaitingLinesOfShipperPage>(parameters => parameters.Add(p => p.ShipperId, "SHP-9999"));

        page.WaitForAssertion(() => page.Markup.Should().Contain("The ERP has no Shipper SHP-9999."));
    }

    [Fact]
    public void RenderShipper_WithoutWaitingLines_SaysSo()
    {
        Answer<ListWaitingBillableLinesOfShipperQuery, WaitingShipperLinesView?>(new WaitingShipperLinesView("SHP-0001", "Brouwer Dranken", 0m, []));

        var page = Render<WaitingLinesOfShipperPage>(parameters => parameters.Add(p => p.ShipperId, "SHP-0001"));

        page.WaitForAssertion(() => page.Markup.Should().Contain("Brouwer Dranken has no Waiting billable lines."));
    }

    [Fact]
    public void RenderShipper_WithOneLine_SaysLineInTheSingular()
    {
        Answer<ListWaitingBillableLinesOfShipperQuery, WaitingShipperLinesView?>(new WaitingShipperLinesView("SHP-0001", "Brouwer Dranken", 15m, [Line("TO-0000001", 15m)]));

        var page = Render<WaitingLinesOfShipperPage>(parameters => parameters.Add(p => p.ShipperId, "SHP-0001"));

        page.WaitForAssertion(() => page.Markup.Should().Contain("1 Waiting line, € 15.00 without VAT."));
    }

    [Fact]
    public void Pages_AreForFinanceOnly()
    {
        typeof(WaitingLinesPage).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == Norvia.Erp.Web.Operators.FinanceSignIn.FinancePolicy);
        typeof(WaitingLinesOfShipperPage).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == Norvia.Erp.Web.Operators.FinanceSignIn.FinancePolicy);
    }
}

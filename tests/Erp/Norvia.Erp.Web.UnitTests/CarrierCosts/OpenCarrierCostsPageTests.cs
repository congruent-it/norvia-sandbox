using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.CarrierCosts;
using Norvia.Erp.Application.CarrierCosts.ListOpen;
using Norvia.Erp.Web.CarrierCosts;
using Norvia.Erp.Web.Live;
using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.Web.UnitTests.CarrierCosts;

public sealed class OpenCarrierCostsPageTests : ErpWebContext
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    public OpenCarrierCostsPageTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = Refresh }));
    }

    private static CarrierCostView Cost(string number, decimal total, CarrierCostSourceView? source = null) => new(
        number,
        "REF-" + number,
        "CAR-0001",
        "SH-0000123",
        "TO-0000123",
        "EUR",
        total,
        [new ChargeLineView("Freight", total - 5m), new ChargeLineView("MinimumChargeAdjustment", 5m)],
        source ?? new CarrierCostSourceView("CarrierRateCard", "CRC-0001", 3, null),
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
        "delivered-1",
        new DateTimeOffset(2026, 10, 18, 11, 0, 0, TimeSpan.Zero),
        "Open");

    private static OpenCarrierCostsOfCarrierView Carrier(params CarrierCostView[] costs) =>
        new("CAR-0001", "Van Dijk Transport", costs.Sum(cost => cost.Total), costs);

    [Fact]
    public void Render_ShowsEachCarriersOpenCostsOnOpening_WithChargeLinesAndSource()
    {
        Answer<ListOpenCarrierCostsQuery, IReadOnlyList<OpenCarrierCostsOfCarrierView>>([Carrier(Cost("CB-0000001", 15m), Cost("CB-0000002", 25m))]);

        var page = Render<OpenCarrierCostsPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Van Dijk Transport"));
        page.Markup.Should().Contain("2 Open costs, € 40.00 without VAT.");
        page.Markup.Should().Contain("REF-CB-0000001 · € 15.00 · delivered 2026-10-17").And.NotContain("Spot price");
        var details = page.FindComponents<CarrierCostDetails>();
        details.Should().HaveCount(2);
        details[0].Markup.Should().Contain("CB-0000001").And.Contain("SH-0000123").And.Contain("TO-0000123").And.Contain("2026-10-17 14:30 +02:00")
            .And.Contain("Carrier Rate Card CRC-0001 version 3").And.Contain("delivered-1")
            .And.Contain("Minimum charge adjustment").And.Contain("€ 5.00").And.Contain("€ 10.00").And.NotContain("Spot price");
    }

    [Fact]
    public void Render_ASpotPrice_IsMarkedWithThePlannersRemark()
    {
        Answer<ListOpenCarrierCostsQuery, IReadOnlyList<OpenCarrierCostsOfCarrierView>>(
            [Carrier(Cost("CB-0000001", 15m, new CarrierCostSourceView("Spot", null, null, "Ferry strike")))]);

        var page = Render<OpenCarrierCostsPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("REF-CB-0000001 · € 15.00 · delivered 2026-10-17 · Spot price"));
        page.Markup.Should().Contain("The Planner's remark: Ferry strike");
        page.Markup.Should().Contain("1 Open cost, € 15.00 without VAT.");
    }

    [Fact]
    public void Render_WithNone_SaysNoCostIsOpen()
    {
        Answer<ListOpenCarrierCostsQuery, IReadOnlyList<OpenCarrierCostsOfCarrierView>>([]);

        var page = Render<OpenCarrierCostsPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("No carrier costs are open"));
    }

    [Fact]
    public void Render_WhenTheFirstReadFails_SaysSoAndKeepsTrying()
    {
        Fail<ListOpenCarrierCostsQuery, IReadOnlyList<OpenCarrierCostsOfCarrierView>>(new InvalidOperationException("database down"));

        var page = Render<OpenCarrierCostsPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("couldn't be loaded"));
    }

    [Fact]
    public void Render_WhenACostArrivesMeanwhile_ListsItWithoutARefresh()
    {
        Answer<ListOpenCarrierCostsQuery, IReadOnlyList<OpenCarrierCostsOfCarrierView>>([Carrier(Cost("CB-0000001", 10m))]);
        var page = Render<OpenCarrierCostsPage>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("1 Open cost"));

        Answer<ListOpenCarrierCostsQuery, IReadOnlyList<OpenCarrierCostsOfCarrierView>>([Carrier(Cost("CB-0000001", 10m), Cost("CB-0000002", 15m))]);
        _time.Advance(Refresh);

        page.WaitForAssertion(() => page.Markup.Should().Contain("2 Open costs, € 25.00"));
    }

    [Fact]
    public void Page_IsForFinanceOnly() =>
        typeof(OpenCarrierCostsPage).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == FinanceSignIn.FinancePolicy);
}

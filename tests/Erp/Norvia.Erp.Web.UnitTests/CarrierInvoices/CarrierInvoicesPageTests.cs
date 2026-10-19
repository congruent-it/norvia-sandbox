using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.List;
using Norvia.Erp.Web.CarrierInvoices;
using Norvia.Erp.Web.Live;
using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.Web.UnitTests.CarrierInvoices;

public sealed class CarrierInvoicesPageTests : ErpWebContext
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    public CarrierInvoicesPageTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = Refresh }));
    }

    private static CarrierInvoiceLineView Line(string result, decimal amount = 10m) =>
        new("TRX-1", amount, null, result, result == "Matched" ? "Matched" : "ToDecide", result == "UnknownBooking" ? null : "CB-0000001", result == "Matched" ? amount : null);

    private static CarrierInvoiceListItem Item(string number, params CarrierInvoiceLineView[] lines)
    {
        var approved = lines.All(line => line.Status == "Matched");
        var total = lines.Sum(line => line.Amount);
        return new CarrierInvoiceListItem(
            "Van Dijk Transport",
            new CarrierInvoiceView(
                "CAR-0001", number, new DateOnly(2026, 10, 19), "EUR", total, lines, "invoice-1", DateTimeOffset.UnixEpoch,
                approved ? "Approved" : "Received", approved ? total : null, approved ? DateTimeOffset.UnixEpoch : null));
    }

    [Fact]
    public void Render_ShowsEachInvoiceWithStatusApprovedAmountAndLineResults()
    {
        Answer<ListCarrierInvoicesQuery, IReadOnlyList<CarrierInvoiceListItem>>(
            [Item("VDT-2", Line("Matched", 70.50m), Line("Matched", 20m)), Item("VDT-1", Line("PriceMismatch"), Line("UnknownBooking"), Line("Matched"))]);

        var page = Render<CarrierInvoicesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-2"));
        var rows = page.FindAll("tbody tr, [role=row]").Select(row => row.TextContent).ToList();
        var approved = rows.Single(row => row.Contains("VDT-2", StringComparison.Ordinal));
        approved.Should().Contain("Van Dijk Transport").And.Contain("2026-10-19").And.Contain("Approved").And.Contain("€ 90.50").And.Contain("2 Matched");
        var received = rows.Single(row => row.Contains("VDT-1", StringComparison.Ordinal));
        received.Should().Contain("Received").And.Contain("1 Price mismatch, 1 Unknown booking, 1 Matched").And.Contain("–").And.Contain("€ 30.00");
    }

    [Fact]
    public void Render_WithNone_SaysNoInvoiceWasReceived()
    {
        Answer<ListCarrierInvoicesQuery, IReadOnlyList<CarrierInvoiceListItem>>([]);

        var page = Render<CarrierInvoicesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("No Carrier invoice has been received yet"));
    }

    [Fact]
    public void Render_WhenTheFirstReadFails_SaysSoAndKeepsTrying()
    {
        Fail<ListCarrierInvoicesQuery, IReadOnlyList<CarrierInvoiceListItem>>(new InvalidOperationException("database down"));

        var page = Render<CarrierInvoicesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("couldn't be loaded"));
    }

    [Fact]
    public void Render_WhenAnInvoiceArrivesMeanwhile_ListsItWithoutARefresh()
    {
        Answer<ListCarrierInvoicesQuery, IReadOnlyList<CarrierInvoiceListItem>>([Item("VDT-1", Line("Matched"))]);
        var page = Render<CarrierInvoicesPage>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-1"));

        Answer<ListCarrierInvoicesQuery, IReadOnlyList<CarrierInvoiceListItem>>([Item("VDT-2", Line("Duplicate")), Item("VDT-1", Line("Matched"))]);
        _time.Advance(Refresh);

        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-2"));
    }

    [Fact]
    public void Page_IsForFinanceOnly() =>
        typeof(CarrierInvoicesPage).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == FinanceSignIn.FinancePolicy);
}

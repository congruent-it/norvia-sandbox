using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.List;
using Norvia.Erp.Application.ShipperInvoices.Open;
using Norvia.Erp.Web.Live;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class ShipperInvoicePagesTests : ErpWebContext
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    public ShipperInvoicePagesTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = Refresh }));
    }

    [Fact]
    public void RenderList_ShowsEachInvoiceWithNumberShipperDateAndTotals_LinkingToIt()
    {
        Answer<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>([ShipperInvoicesTestData.Invoice(2, 1000m), ShipperInvoicesTestData.Invoice(1, 100m)]);

        var page = Render<ShipperInvoicesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("INV-2026-000002"));
        page.FindAll("td").Select(cell => cell.TextContent.Trim()).Should().ContainInOrder(
            "INV-2026-000002", "Brouwer Dranken", "2026-10-19", "€ 1,000.00", "€ 210.00", "€ 1,210.00", "Issued", "—",
            "INV-2026-000001", "Brouwer Dranken", "2026-10-19", "€ 100.00", "€ 21.00", "€ 121.00", "Issued", "—");
        page.Find("a[href='/shipper-invoices/INV-2026-000002']").TextContent.Should().Be("INV-2026-000002");
    }

    [Fact]
    public void RenderList_ShowsTheSentTimeInAmsterdam_OfAnInvoiceReportedSent_AndNoneForOneThatWasNot()
    {
        Answer<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>(
            [ShipperInvoicesTestData.Invoice(2), ShipperInvoicesTestData.Invoice(1, sentAt: new DateTimeOffset(2026, 10, 19, 8, 0, 5, TimeSpan.Zero))]);

        var page = Render<ShipperInvoicesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("INV-2026-000002"));
        var cells = page.FindAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        cells.Should().ContainInOrder("INV-2026-000002", "Issued", "—", "INV-2026-000001", "Sent", "2026-10-19 10:00:05");
    }

    [Fact]
    public void RenderList_WithNoInvoices_SaysNoneWasIssued()
    {
        Answer<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>([]);

        var page = Render<ShipperInvoicesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("No Shipper invoice has been issued yet."));
    }

    [Fact]
    public void RenderList_WhenAnInvoiceIsIssuedMeanwhile_ListsItWithoutARefresh()
    {
        Answer<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>([ShipperInvoicesTestData.Invoice(1)]);
        var page = Render<ShipperInvoicesPage>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("INV-2026-000001").And.NotContain("INV-2026-000002"));

        Answer<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>([ShipperInvoicesTestData.Invoice(2), ShipperInvoicesTestData.Invoice(1)]);
        _time.Advance(Refresh);

        page.WaitForAssertion(() => page.Markup.Should().Contain("INV-2026-000002"));
    }

    [Fact]
    public void RenderList_WhenTheFirstReadFails_SaysSo()
    {
        Fail<ListShipperInvoicesQuery, IReadOnlyList<ShipperInvoiceView>>(new InvalidOperationException("database down"));

        var page = Render<ShipperInvoicesPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("couldn't be loaded"));
    }

    [Fact]
    public void RenderInvoice_ShowsTheHeaderTotalsAndEachLineOnOpening()
    {
        Answer<OpenShipperInvoiceQuery, ShipperInvoiceView?>(ShipperInvoicesTestData.Invoice(3, 200m, lines: 2));

        var page = Render<ShipperInvoicePage>(parameters => parameters.Add(p => p.InvoiceNumber, "INV-2026-000003"));

        page.WaitForAssertion(() => page.Markup.Should().Contain("Shipper invoice INV-2026-000003"));
        page.Markup.Should().Contain("Brouwer Dranken (SHP-0001)").And.Contain("2026-10-19").And.Contain("Issued")
            .And.Contain("€ 200.00").And.Contain("€ 42.00").And.Contain("€ 242.00");
        page.Markup.Should().Contain("TO-0000001 · € 100.00").And.Contain("TO-0000002 · € 100.00");
        page.Markup.Should().Contain("PO-1").And.Contain("SH-0000001").And.Contain("2026-10-17").And.Contain("MinimumChargeAdjustment").And.Contain("€ 95.00");
    }

    [Fact]
    public void RenderInvoice_ANumberWithoutAnInvoice_SaysTheErpHasNone()
    {
        Answer<OpenShipperInvoiceQuery, ShipperInvoiceView?>(null);

        var page = Render<ShipperInvoicePage>(parameters => parameters.Add(p => p.InvoiceNumber, "INV-2026-999999"));

        page.WaitForAssertion(() => page.Markup.Should().Contain("The ERP has no Shipper invoice INV-2026-999999."));
    }

    [Fact]
    public void Pages_AreForFinanceOnly()
    {
        foreach (var page in new[] { typeof(ShipperInvoicesPage), typeof(ShipperInvoicePage) })
        {
            page.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .Should().ContainSingle(attribute => attribute.Policy == Norvia.Erp.Web.Operators.FinanceSignIn.FinancePolicy);
        }
    }
}

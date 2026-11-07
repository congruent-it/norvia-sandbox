using System.Globalization;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;

namespace Norvia.Erp.Web.UiTests;

/// <summary>
/// A fresh browser page per test, on the ERP UI, after the ERP has been emptied of what earlier tests recorded (<see cref="TracedPage"/> keeps the trace and
/// video of a failed test).
/// </summary>
public abstract class BrowserPage(ErpUiFixture erp) : TracedPage(erp.Browser, erp.ErpUi)
{
    protected const string Brouwer = "SHP-0001";
    protected const string BrouwerName = "Brouwer Dranken";

    protected ErpUiFixture Erp { get; } = erp;

    protected override Task PrepareAsync() => Erp.ResetAsync(Ct);

    /// <summary>A Transport Order number that looks like the TMS's (TO- and seven digits), and is not one a test recorded before it.</summary>
    protected static string NewTransportOrderNumber() => string.Create(CultureInfo.InvariantCulture, $"TO-{Random.Shared.Next(1_000_000, 10_000_000)}");

    /// <summary>Records a Billable line for a Shipper, as the Billing Logic App does, and returns its Transport Order number.</summary>
    protected async Task<string> RecordLineAsync(string shipperId = Brouwer, decimal freight = 100m, decimal minimumChargeAdjustment = 0m)
    {
        var number = NewTransportOrderNumber();
        await Erp.RecordBillableLineAsync(number, shipperId, freight, minimumChargeAdjustment, Ct);
        return number;
    }

    /// <summary>Opens a view: the section in the main navigation, then the view in the strip of that section's views below it.</summary>
    protected async Task OpenViewAsync(string section, string view)
    {
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = section, Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Navigation, new() { Name = section, Exact = true }).GetByRole(AriaRole.Link, new() { Name = view, Exact = true }).ClickAsync();
    }
}

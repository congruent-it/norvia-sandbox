using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.Bookings;

/// <summary>
/// Carrier invoice matching (E2), from the Carrier Persona's side in the Partner portal and from Finance's in the ERP UI. A Shipment is booked, planned, released,
/// confirmed and delivered through the screens, so its Carrier cost reaches the ERP as it does in the demo. Each journey delivers what it invoices and invoices only that,
/// whatever other journeys left on the draft invoice. It belongs to the booking journeys' class, which runs its tests one at a time, as one takes the TMS down.
/// The edge cases (rejections, repeats, a late Carrier cost, a Carrier the ERP doesn't know) are in the integration tests.
/// </summary>
public sealed partial class BookingJourneyTests
{
    private const string VanDijk = "van-dijk-transport";
    private const string VanDijkName = "Van Dijk Transport";

    // A delivery goes through the TMS and the Carrier cost worker before the ERP lists the Carrier cost, and an outcome through the Carrier invoice outcome worker to the portal.
    private const float PatienceMilliseconds = 60_000;

    [Fact]
    public async Task ACarrier_SendsTheDraftInvoiceWithAnEditedAmount_AndSeesEachLinesResultStraightAwayOnTheInvoicesPage()
    {
        var delivered = await DeliveredBookingAsync();
        var invoiceNumber = NewInvoiceNumber();
        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/{VanDijk}/draft-invoice").ToString());
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Draft invoice", Level = 1 })).ToBeVisibleAsync();

        // The draft lists the delivered, not yet invoiced booking with its agreed price, which the Carrier edits.
        var drafts = Portal.GetByRole(AriaRole.Table, new() { Name = "Bookings to invoice" });
        var draft = drafts.GetByRole(AriaRole.Row).Filter(new() { HasText = delivered.Reference });
        await Expect(draft).ToContainTextAsync(delivered.BookingNumber);
        await Expect(draft).ToContainTextAsync("€");
        var agreed = await AgreedAmountAsync(delivered.Reference);
        var claimed = agreed + 10m;
        await InvoiceOnlyAsync(delivered.Reference);
        await Portal.GetByRole(AriaRole.Textbox, new() { Name = "Invoice number", Exact = true }).FillAsync(invoiceNumber);
        await Portal.GetByRole(AriaRole.Textbox, new() { Name = $"Amount for {delivered.Reference}", Exact = true }).FillAsync(Amount(claimed));
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Send invoice" }).ClickAsync();

        // Norvia answers on the spot with the result of the line: the amount is not the agreed price.
        var result = Portal.GetByRole(AriaRole.Region, new() { Name = $"Result of invoice {invoiceNumber}" });
        await Expect(result).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
        var line = result.GetByRole(AriaRole.Row).Filter(new() { HasText = delivered.Reference });
        await Expect(line).ToContainTextAsync("Price mismatch");
        await Expect(line).ToContainTextAsync(Money(claimed));
        await Expect(drafts.GetByRole(AriaRole.Row).Filter(new() { HasText = delivered.Reference })).ToHaveCountAsync(0);

        // The invoices page shows the invoice with the same result and no outcome yet.
        await Portal.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Invoices" }).ClickAsync();
        var invoice = Portal.GetByRole(AriaRole.Article, new() { Name = invoiceNumber });
        await Expect(invoice).ToBeVisibleAsync();
        await Expect(invoice).ToContainTextAsync("Received");
        var row = invoice.GetByRole(AriaRole.Row).Filter(new() { HasText = delivered.Reference });
        await Expect(row).ToContainTextAsync("Price mismatch");
        await Expect(row).ToContainTextAsync("Waiting for Norvia");
    }

    [Fact]
    public async Task FinanceAcceptsAPriceMismatch_AtTheAgreedAmount_OffersRejectOnlyForAnUnknownBooking_AndTheCarriersPageShowsTheOutcomePerLine()
    {
        // The second booking's Carrier cost doesn't reach the ERP, so the Carrier invoices a booking the ERP doesn't know yet.
        var mismatch = await DeliveredBookingAsync();
        DeliveredBooking unknown;
        await _norvia.StopCarrierCostWorkerAsync(Ct);
        try
        {
            unknown = await DeliveredBookingAsync(waitForCarrierCost: false);
            var invoiceNumber = NewInvoiceNumber();
            var agreed = await SendDraftInvoiceAsync(invoiceNumber, (mismatch.Reference, 10m), (unknown.Reference, 0m));

            // Finance sees the invoice with a line to decide per result, and the Carrier cost of the mismatch line next to it.
            await Erp.GotoAsync(new Uri(_norvia.ErpUi, "/carrier-invoices/to-decide").ToString());
            var section = Erp.GetByRole(AriaRole.Region, new() { Name = $"Carrier invoice {invoiceNumber} of {VanDijkName}" });
            await Expect(section).ToContainTextAsync("1 Price mismatch, 1 Unknown booking", new() { Timeout = PatienceMilliseconds });

            await OpenLineAsync(section, mismatch.Reference);
            var cost = section.GetByLabel($"Carrier cost {mismatch.BookingNumber}", new() { Exact = true });
            await Expect(cost).ToContainTextAsync(mismatch.Reference);
            await Expect(cost).ToContainTextAsync("Carrier Rate Card");
            await Expect(section.GetByRole(AriaRole.Button, new() { Name = $"Accept {mismatch.Reference}" })).ToBeVisibleAsync();

            // Accepting is offered for the line with an Open Carrier cost only; the Unknown booking line offers reject.
            await OpenLineAsync(section, unknown.Reference);
            await Expect(section.GetByText("No Carrier cost has arrived for this line.")).ToBeVisibleAsync();
            await Expect(section.GetByRole(AriaRole.Button, new() { Name = $"Accept {unknown.Reference}" })).ToHaveCountAsync(0);
            await Expect(section.GetByRole(AriaRole.Button, new() { Name = $"Reject {unknown.Reference}" })).ToBeVisibleAsync();

            await section.GetByRole(AriaRole.Textbox, new() { NameRegex = new Regex($"^Adjusted amount for {Regex.Escape(mismatch.Reference)}") }).FillAsync(Amount(agreed[mismatch.Reference]));
            await section.GetByRole(AriaRole.Textbox, new() { Name = $"Remark on the adjusted amount for {mismatch.Reference}" }).FillAsync("Agreed price applies");
            await section.GetByRole(AriaRole.Button, new() { Name = $"Accept {mismatch.Reference}" }).ClickAsync();
            await Expect(section.GetByRole(AriaRole.Button, new() { Name = $"Accept {mismatch.Reference}" })).ToHaveCountAsync(0);

            await section.GetByRole(AriaRole.Textbox, new() { Name = $"Reason for rejecting {unknown.Reference}" }).FillAsync("No such booking");
            await section.GetByRole(AriaRole.Button, new() { Name = $"Reject {unknown.Reference}" }).ClickAsync();

            // With the last line decided the invoice is Approved: it leaves the list of invoices to decide and shows as Approved in the list of all.
            await Expect(section).ToHaveCountAsync(0, new() { Timeout = PatienceMilliseconds });
            await Erp.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Carrier invoicing", Exact = true }).ClickAsync();
            await Erp.GetByRole(AriaRole.Navigation, new() { Name = "Carrier invoicing", Exact = true }).GetByRole(AriaRole.Link, new() { Name = "Carrier invoices", Exact = true }).ClickAsync();
            await Expect(Erp.GetByRole(AriaRole.Row).Filter(new() { HasText = invoiceNumber })).ToContainTextAsync("Approved", new() { Timeout = PatienceMilliseconds });

            // The Carrier's invoices page shows the outcome of each line, as Norvia told it, without a refresh.
            await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/{VanDijk}/carrier-invoices").ToString());
            var invoice = Portal.GetByRole(AriaRole.Article, new() { Name = invoiceNumber });
            await Expect(invoice).ToContainTextAsync("Approved", new() { Timeout = PatienceMilliseconds });
            var accepted = invoice.GetByRole(AriaRole.Row).Filter(new() { HasText = mismatch.Reference });
            await Expect(accepted).ToContainTextAsync($"Accepted, adjusted at {Money(agreed[mismatch.Reference])}");
            await Expect(accepted).ToContainTextAsync("Agreed price applies");
            var rejected = invoice.GetByRole(AriaRole.Row).Filter(new() { HasText = unknown.Reference });
            await Expect(rejected).ToContainTextAsync("Rejected");
            await Expect(rejected).ToContainTextAsync("No such booking");
        }
        finally
        {
            await _norvia.StartCarrierCostWorkerAsync(Ct);
        }
    }

    [Fact]
    public async Task TheOpenCarrierCostsList_ShowsTheCarrierCostPerCarrier_AndMarksASpotPrice()
    {
        // Van Dijk Transport's Carrier Rate Card has no Lane into Germany, so the Planner releases this Shipment with a Spot price.
        var spot = await DeliveredBookingAsync(spotPrice: "480.50");

        await Erp.GotoAsync(new Uri(_norvia.ErpUi, "/carrier-costs").ToString());
        await Expect(Erp.GetByRole(AriaRole.Heading, new() { Name = "Open carrier costs", Level = 1 })).ToBeVisibleAsync();
        var carrier = Erp.GetByRole(AriaRole.Region, new() { Name = $"Open carrier costs of {VanDijkName}" });
        await Expect(carrier).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
        await Expect(carrier.GetByRole(AriaRole.Heading, new() { Name = VanDijkName, Level = 2 })).ToBeVisibleAsync();
        var cost = carrier.GetByRole(AriaRole.Button, new() { Name = spot.Reference });
        await Expect(cost).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
        // The heading's words are the button's name.
        await Expect(cost).ToHaveAccessibleNameAsync(new Regex("€ 480.50 · delivered .* · Spot price"));

        await cost.ClickAsync();
        await Expect(carrier.GetByText("Spot price. The Planner's remark: agreed with Van Dijk by phone")).ToBeVisibleAsync();
    }

    /// <summary>A Shipment of Brouwer Dranken, carried by Van Dijk Transport and delivered, with the Carrier booking it was confirmed as.</summary>
    private sealed record DeliveredBooking(string TransportOrderNumber, string BookingNumber, string Reference);

    /// <summary>
    /// Books a transport as the Shipper, has the Planner assign it to Van Dijk Transport and release it, has Van Dijk confirm the booking in its inbox and report the Shipment
    /// Collected, Departed and Delivered, and waits until the ERP lists its Carrier cost as Open.
    /// </summary>
    private async Task<DeliveredBooking> DeliveredBookingAsync(string? spotPrice = null, bool waitForCarrierCost = true)
    {
        var shipperReference = NewReference();
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", shipperReference);
        if (spotPrice is not null)
        {
            await FillAsync("Delivery", "Postcode", "40212");
            await FillAsync("Delivery", "City", "Düsseldorf");
            await FillAsync("Delivery", "Country", "DE");
        }

        await BookAsync();
        var transportOrder = await AcceptedNumberAsync();

        var shipmentNumber = await PlanAndReleaseAsync(transportOrder, shipperReference, spotPrice);

        // The booking request reaches Van Dijk's inbox, and the Carrier confirms it with a reference of its own.
        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/{VanDijk}/booking-requests").ToString());
        var request = Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(request).ToContainTextAsync("CB-", new() { Timeout = PatienceMilliseconds });
        var bookingNumber = BookingNumberPattern().Match(await request.InnerTextAsync()).Value;
        var reference = $"VDT-{Guid.NewGuid():N}"[..12];
        await request.GetByRole(AriaRole.Button, new() { Name = $"Confirm {bookingNumber}", Exact = true }).ClickAsync();
        await Portal.GetByRole(AriaRole.Textbox, new() { Name = "Carrier booking reference", Exact = true }).FillAsync(reference);
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Confirm booking" }).ClickAsync();

        // It reports the Shipment on its way and delivered.
        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/{VanDijk}/shipments").ToString());
        var shipment = Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = bookingNumber });
        foreach (var event_ in new[] { "Collected (PU)", "Departed (DEP)", "Delivered (POD)" })
        {
            await shipment.GetByRole(AriaRole.Button, new() { Name = $"{event_} for {bookingNumber}", Exact = true }).ClickAsync(new() { Timeout = PatienceMilliseconds });
        }

        await Expect(shipment).ToContainTextAsync("Delivered");

        if (waitForCarrierCost)
        {
            await Erp.GotoAsync(new Uri(_norvia.ErpUi, "/carrier-costs").ToString());
            await Expect(Erp.GetByRole(AriaRole.Region, new() { Name = $"Open carrier costs of {VanDijkName}" }).GetByRole(AriaRole.Button, new() { Name = reference }))
                .ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
        }

        return new(transportOrder, bookingNumber, reference);
    }

    /// <summary>The Planner assigns the Transport Order to Van Dijk Transport and releases the Shipment, with a Spot price when the Carrier Rate Card can't price it.</summary>
    private async Task<string> PlanAndReleaseAsync(string transportOrder, string shipperReference, string? spotPrice)
    {
        await Tms.GotoAsync(_norvia.TmsUi.ToString());
        await Tms.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await OpenTmsViewAsync("Waiting for planning");
        var order = Tms.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await order.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {transportOrder}" }).ClickAsync();
        var assign = Tms.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Tms.GetByRole(AriaRole.Option, new() { Name = VanDijkName }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();
        var planned = Tms.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status);
        await Expect(planned).ToContainTextAsync($"Transport Order {transportOrder} is planned as Shipment");
        var shipmentNumber = (await planned.GetByRole(AriaRole.Link).TextContentAsync())!;

        await OpenTmsViewAsync("Shipments to release");
        var toRelease = Tms.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        if (spotPrice is null)
        {
            await toRelease.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {shipmentNumber}" }).ClickAsync();
            await Tms.GetByRole(AriaRole.Group, new() { Name = "Release" }).GetByRole(AriaRole.Button, new() { Name = "Release selected" }).ClickAsync();
        }
        else
        {
            await toRelease.GetByRole(AriaRole.Button, new() { Name = $"Release {shipmentNumber} with Spot price" }).ClickAsync();
            var form = Tms.GetByRole(AriaRole.Group, new() { Name = $"Release {shipmentNumber} with Spot price" });
            await form.GetByRole(AriaRole.Textbox, new() { Name = "Amount (EUR, without VAT)" }).FillAsync(spotPrice);
            await form.GetByRole(AriaRole.Textbox, new() { Name = "Remark" }).FillAsync("agreed with Van Dijk by phone");
            await form.GetByRole(AriaRole.Button, new() { Name = "Release with Spot price" }).ClickAsync();
        }

        await Expect(Tms.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Status))
            .ToContainTextAsync($"Shipment {shipmentNumber} is released.");
        return shipmentNumber;
    }

    /// <summary>
    /// Sends one invoice for the given bookings, each with its agreed price plus the given difference, from the draft invoice, and returns the agreed price of each.
    /// The draft also lists what other journeys delivered and left uninvoiced, which this invoice leaves out.
    /// </summary>
    private async Task<Dictionary<string, decimal>> SendDraftInvoiceAsync(string invoiceNumber, params (string Reference, decimal Difference)[] lines)
    {
        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, $"/personas/{VanDijk}/draft-invoice").ToString());
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Draft invoice", Level = 1 })).ToBeVisibleAsync();
        var agreed = new Dictionary<string, decimal>();
        foreach (var (reference, _) in lines)
        {
            agreed[reference] = await AgreedAmountAsync(reference);
        }

        await InvoiceOnlyAsync([.. lines.Select(line => line.Reference)]);
        await Portal.GetByRole(AriaRole.Textbox, new() { Name = "Invoice number", Exact = true }).FillAsync(invoiceNumber);
        foreach (var (reference, difference) in lines)
        {
            await Portal.GetByRole(AriaRole.Textbox, new() { Name = $"Amount for {reference}", Exact = true }).FillAsync(Amount(agreed[reference] + difference));
        }

        await Portal.GetByRole(AriaRole.Button, new() { Name = "Send invoice" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Region, new() { Name = $"Result of invoice {invoiceNumber}" })).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
        return agreed;
    }

    /// <summary>The amount the draft invoice fills in for a booking: its agreed price.</summary>
    private async Task<decimal> AgreedAmountAsync(string reference)
    {
        var amount = Portal.GetByRole(AriaRole.Textbox, new() { Name = $"Amount for {reference}", Exact = true });
        await Expect(amount).ToBeVisibleAsync(new() { Timeout = PatienceMilliseconds });
        return decimal.Parse(await amount.InputValueAsync(), CultureInfo.InvariantCulture);
    }

    /// <summary>Selects the given bookings on the draft invoice and no other.</summary>
    private async Task InvoiceOnlyAsync(params string[] references)
    {
        var checkboxes = Portal.GetByRole(AriaRole.Table, new() { Name = "Bookings to invoice" }).GetByRole(AriaRole.Checkbox);
        foreach (var checkbox in await checkboxes.AllAsync())
        {
            var label = await checkbox.GetAttributeAsync("aria-label");
            var selected = references.Any(reference => label == $"Invoice {reference}");
            if (selected != await checkbox.IsCheckedAsync())
            {
                await checkbox.SetCheckedAsync(selected);
            }
        }
    }

    /// <summary>Opens a line of a Carrier invoice in Finance's list, by its Carrier booking reference.</summary>
    private static async Task OpenLineAsync(ILocator invoice, string reference)
    {
        // The heading reads "reference · amount · result · status", unlike the Accept and Reject buttons of the line.
        var heading = invoice.GetByRole(AriaRole.Button, new() { NameRegex = new Regex($"^{Regex.Escape(reference)} · ") });
        if (await heading.GetAttributeAsync("aria-expanded") != "true")
        {
            await heading.ClickAsync();
        }

        await Expect(heading).ToHaveAttributeAsync("aria-expanded", "true");
    }

    private async Task OpenTmsViewAsync(string title)
    {
        var navigation = Tms.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        var link = navigation.GetByRole(AriaRole.Link, new() { Name = title, Exact = true });
        if (!await link.IsVisibleAsync())
        {
            await navigation.Locator("details").Filter(new() { HasText = title }).Locator("summary").First.ClickAsync();
        }

        await link.ClickAsync();
    }

    // Unique per test run, as a Carrier uses an invoice number once.
    private static string NewInvoiceNumber() => $"INV-UI-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    private static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => string.Create(CultureInfo.InvariantCulture, $"€ {amount:N2}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("CB-[0-9]{7}")]
    private static partial Regex BookingNumberPattern();
}

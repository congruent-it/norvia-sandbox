using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>
/// An operator runs a plan whose Carriers deliver and prove every delivery, some with reservations, and the Planner opens a Shipment's tracking timeline in the TMS UI:
/// the Proofs of delivery grid links the Carrier's PDF, which reads as a delivery receipt (#522). The document's other lines and its size are in the integration tests.
/// </summary>
public sealed partial class ProofOfDeliveryJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 20;
    private const string ReservationsRemark = "Two pallets short";

    // The TMS processes what the Carriers send after it accepts it, so the Proof of delivery shows a little after the run.
    private const float PatienceMilliseconds = 90_000;

    [Fact]
    public async Task ThePlannerOpensTheTimelineOfADeliveredShipment_AndReadsTheCarriersPdf_WithTheRemarkOnlyForReservations()
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        // Every booking is good, planned and released, every Carrier confirms, tracks, delivers and proves the delivery, and half of them hit an Exception.
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 0), ("Reassigned (%)", 0),
            ("Released by the Planner (%)", 100), ("Confirmed by the Carrier (%)", 100), ("Declined by the Carrier (%)", 0), ("Tracked (%)", 100),
            ("Exceptions on the way (%)", 50), ("Delivered (%)", 100), ("With a Proof of delivery (%)", 100), ("Failed collections cancelled (%)", 0),
        })
        {
            await Page.GetByLabel(label).FillAsync(percent.ToString(CultureInfo.InvariantCulture));
        }

        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        await Expect(Page.GetByRole(AriaRole.Status).First).ToContainTextAsync("Completed", new() { Timeout = PatienceMilliseconds });
        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        var proofs = log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator("[data-outcome=ProofOfDeliverySent]") });
        var reserved = proofs.Filter(new() { HasText = "With reservations" });
        var clean = proofs.Filter(new() { HasText = "Clean" });
        (await reserved.CountAsync()).Should().BeGreaterThan(0, "some deliveries have reservations");
        (await clean.CountAsync()).Should().BeGreaterThan(0, "some deliveries are clean");

        var reservedText = await ReadReceiptAsync(reserved.First);
        reservedText.Should().Contain("Condition: Reservations").And.Contain($"Remark: {ReservationsRemark}");

        var cleanText = await ReadReceiptAsync(clean.First);
        cleanText.Should().Contain("Condition: Clean").And.NotContain("Remark:");
    }

    // Opens the log line's Shipment in the TMS UI, follows the Proofs of delivery grid's link to the PDF and gives what the PDF says,
    // after checking that it names the Carrier of the Shipment's booking and the Shipment number.
    private async Task<string> ReadReceiptAsync(ILocator logLine)
    {
        var shipmentLink = logLine.GetByRole(AriaRole.Link, new() { NameRegex = ShipmentLinkName() });
        var shipmentNumber = (await shipmentLink.InnerTextAsync()).Trim();
        var tms = await Page.Context.RunAndWaitForPageAsync(() => shipmentLink.ClickAsync());
        await Expect(tms).ToHaveURLAsync(new Regex($"/shipments/{Regex.Escape(shipmentNumber)}$"));

        var file = tms.GetByRole(AriaRole.Region, new() { Name = "Proofs of delivery" }).GetByRole(AriaRole.Link, new() { NameRegex = PdfName() });
        await WaitForProofAsync(tms, file);

        var carrier = await CarrierOfAsync(tms);
        var href = new Uri(new Uri(tms.Url), await file.First.GetAttributeAsync("href"));
        var response = await Page.Context.APIRequest.GetAsync(href.ToString());
        response.Status.Should().Be(200);
        response.Headers["content-type"].Should().Be("application/pdf");
        var text = TextOf(await response.BodyAsync());

        text.Should().StartWith($"Proof of delivery\n{carrier}\n", "a delivery receipt names the Carrier under its heading").And.Contain($"Shipment: {shipmentNumber}");
        await tms.CloseAsync();
        return text;
    }

    // The TMS files the Carrier's proof a little after the Simulator sent it, so the page is reloaded until the file is linked.
    private static async Task WaitForProofAsync(IPage tms, ILocator file)
    {
        var timer = Stopwatch.StartNew();
        while (await file.CountAsync() == 0)
        {
            if (timer.ElapsedMilliseconds > PatienceMilliseconds)
            {
                await Expect(file.First).ToBeVisibleAsync(new() { Timeout = 1 });
            }

            await Task.Delay(2_000);
            await tms.ReloadAsync();
            await Expect(tms.GetByRole(AriaRole.Heading, new() { Name = "Tracking timeline" })).ToBeVisibleAsync();
        }
    }

    // The Carrier column of the Shipment's Carrier cost grid.
    private static async Task<string> CarrierOfAsync(IPage tms)
    {
        var row = tms.GetByRole(AriaRole.Table, new() { Name = "Carrier cost by Carrier booking" }).GetByRole(AriaRole.Row).Nth(1);
        await Expect(row).ToBeVisibleAsync();
        return (await row.GetByRole(AriaRole.Gridcell).Nth(1).InnerTextAsync()).Trim();
    }

    // The text a one-page PDF shows, one line per Tj operator: the string in brackets, with its escaped brackets and backslashes unescaped.
    private static string TextOf(byte[] pdf)
    {
        var lines = ShownText().Matches(Encoding.Latin1.GetString(pdf)).Select(match => Regex.Replace(match.Groups[1].Value, @"\\(.)", "$1"));
        return string.Join('\n', lines);
    }

    [GeneratedRegex("Open Shipment SH-.* in the TMS")]
    private static partial Regex ShipmentLinkName();

    [GeneratedRegex(@"^\S+\.pdf$")]
    private static partial Regex PdfName();

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\) Tj")]
    private static partial Regex ShownText();
}

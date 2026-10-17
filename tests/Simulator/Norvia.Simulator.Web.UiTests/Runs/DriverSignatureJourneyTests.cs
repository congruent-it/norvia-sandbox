using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>
/// After a run whose Own fleet Trip the Drivers work to the end, half of the deliveries with reservations, the Planner opens the Shipments' tracking timelines
/// in the TMS and finds each Proof of delivery with a named receiver and a drawn signature, a photo next to it where there were reservations (#525).
/// </summary>
public sealed class DriverSignatureJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 8;
    private const string FallbackReceiver = "Simulated consignee";

    // The Trip completes a little after the run starts (see TripJourneyTests), and the Drivers' reports reach the TMS through a worker after that.
    private const float PatienceMilliseconds = 180_000;

    private readonly SimulatorUiFixture _simulator = simulator;

    [Fact(SkipUnless = nameof(CollectionHours.TodayIsStillOpen), SkipType = typeof(CollectionHours), Skip = CollectionHours.Reason)]
    public async Task ThePlanner_OpensTheTimelinesOfTwoDeliveredShipments_AndSeesTheirDifferentDrawnSignatures_AndAPhotoOnlyWhereThereWereReservations()
    {
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync(_simulator.SignatureFleet.ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        // Every order goes to the Own fleet and is collected today, nothing goes wrong, and half of the deliveries carry reservations.
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Collected today (%)", 100), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 100),
            ("Reassigned (%)", 0), ("Own fleet put on Trips (%)", 100), ("Collections that fail (%)", 0), ("Deliveries that fail (%)", 0),
            ("Damage reported (%)", 0), ("Delivered with reservations (%)", 50),
        })
        {
            await Page.GetByLabel(label).FillAsync(percent.ToString(CultureInfo.InvariantCulture));
        }

        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        await Expect(Rows(log, "TripCompleted")).ToHaveCountAsync(1, new() { Timeout = PatienceMilliseconds });

        // The deliveries the Drivers reported, the clean ones apart from those with reservations (the log says which in the line's detail).
        var deliveries = Rows(log, "DriverReported").Filter(new() { HasText = "Deliver" });
        var reserved = await ShipmentsOfAsync(deliveries.Filter(new() { HasText = "With reservations" }));
        var clean = await ShipmentsOfAsync(deliveries.Filter(new() { HasNotText = "With reservations" }));
        reserved.Should().NotBeEmpty("half of the deliveries carry reservations");
        clean.Should().NotBeEmpty("half of the deliveries are clean");

        var tms = await Page.Context.NewPageAsync();
        var signatures = new List<byte[]>();

        // A delivery with reservations: a named receiver, the signature and a photo next to it, both of which open as pictures.
        var withReservations = await OpenProofsAsync(tms, reserved[0]);
        var receiver = withReservations.GetByRole(AriaRole.Row).Nth(1);
        await Expect(receiver).ToContainTextAsync("With reservations", new() { Timeout = PatienceMilliseconds });
        await Expect(receiver).Not.ToContainTextAsync(FallbackReceiver);
        var signature = receiver.GetByRole(AriaRole.Link, new() { Name = "signature.png", Exact = true });
        await Expect(signature).ToBeVisibleAsync();
        var photo = receiver.GetByRole(AriaRole.Link, new() { NameRegex = new("^photo-.*\\.png$") });
        await Expect(photo).ToHaveCountAsync(1);
        await AssertOpensAsPictureAsync(photo);
        var (width, height) = await AssertOpensAsPictureAsync(signature);
        (width, height).Should().NotBe((1, 1), "the signature is drawn, not the 1x1 placeholder");
        signatures.Add(await BytesOfAsync(tms, signature));
        // Locators look up their element again, so the row's text is read before the page moves on to the other Shipment.
        var reservedProof = await receiver.InnerTextAsync();

        // A clean delivery, of the same run: its own receiver and signature, and no photo.
        var cleanProofs = await OpenProofsAsync(tms, clean[0]);
        var cleanRow = cleanProofs.GetByRole(AriaRole.Row).Nth(1);
        await Expect(cleanRow).ToContainTextAsync("Clean", new() { Timeout = PatienceMilliseconds });
        await Expect(cleanRow).Not.ToContainTextAsync(FallbackReceiver);
        await Expect(cleanRow.GetByRole(AriaRole.Link, new() { NameRegex = new("^photo-") })).ToHaveCountAsync(0);
        var cleanSignature = cleanRow.GetByRole(AriaRole.Link, new() { Name = "signature.png", Exact = true });
        await AssertOpensAsPictureAsync(cleanSignature);
        signatures.Add(await BytesOfAsync(tms, cleanSignature));

        signatures[0].Should().NotEqual(signatures[1], "two Shipments of a run do not show the same signature");
        reservedProof.Should().NotBe(await cleanRow.InnerTextAsync());
    }

    private ILocator Rows(ILocator log, string outcome) =>
        log.GetByRole(AriaRole.Row).Filter(new() { Has = Page.Locator($"[data-outcome={outcome}]") });

    // The Shipment numbers in the links of the given log rows.
    private static async Task<List<string>> ShipmentsOfAsync(ILocator rows)
    {
        var links = rows.GetByRole(AriaRole.Link, new() { NameRegex = new("^Open Shipment ") });
        return [.. (await links.AllInnerTextsAsync()).Distinct()];
    }

    // The Proofs of delivery grid of the Shipment's page in the TMS UI.
    private async Task<ILocator> OpenProofsAsync(IPage tms, string shipmentNumber)
    {
        await tms.GotoAsync(new Uri(_simulator.TmsUi, $"/shipments/{shipmentNumber}").ToString());
        var proofs = tms.GetByRole(AriaRole.Region, new() { Name = "Proofs of delivery" });
        await Expect(proofs.GetByRole(AriaRole.Row)).ToHaveCountAsync(2, new() { Timeout = PatienceMilliseconds });
        return proofs;
    }

    // Follows the file's link into a new tab and returns the size the picture there loads at.
    private async Task<(int Width, int Height)> AssertOpensAsPictureAsync(ILocator link)
    {
        var tab = await Page.Context.RunAndWaitForPageAsync(() => link.ClickAsync());
        await tab.WaitForFunctionAsync("() => document.images.length === 1 && document.images[0].complete && document.images[0].naturalWidth > 0");
        var size = await tab.EvaluateAsync<int[]>("() => [document.images[0].naturalWidth, document.images[0].naturalHeight]");
        await tab.CloseAsync();
        return (size[0], size[1]);
    }

    private static async Task<byte[]> BytesOfAsync(IPage tms, ILocator link)
    {
        var path = await link.GetAttributeAsync("href");
        var response = await tms.APIRequest.GetAsync(new Uri(new Uri(tms.Url), path).ToString());
        response.Status.Should().Be(200);
        return await response.BodyAsync();
    }
}

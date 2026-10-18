using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// The Planner follows a Carrier Shipment's Proofs of delivery: an early one is marked in Shipments in progress, a Delivered
/// Shipment without one waits in Delivered without Proof of delivery until it arrives, and the timeline shows each Proof of
/// delivery with its files, the superseded ones struck through.
/// </summary>
public sealed class ProofOfDeliveryJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private const string VanDijkId = "CAR-0001";

    private readonly TmsUiFixture _tms = tms;

    [Fact]
    public async Task AnEarlyProofOfDelivery_IsMarkedInProgress_AndOnTheTimelineWithItsFile_UntilALaterOneSupersedesIt()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await OpenViewAsync("Shipments in progress");
        var shipment = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(shipment).ToContainTextAsync("In transit");
        await Expect(shipment).Not.ToContainTextAsync("Proof of delivery received");

        await RecordProofAsync(reference, "J. Becker", "cmr-early.pdf");
        await Expect(shipment).ToContainTextAsync("Proof of delivery received");

        await shipment.GetByRole(AriaRole.Link, new() { Name = shipmentNumber }).ClickAsync();
        var proofs = Page.GetByRole(AriaRole.Region, new() { Name = "Proofs of delivery" });
        var rows = proofs.GetByRole(AriaRole.Row);
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(rows.Nth(1)).ToContainTextAsync("J. Becker");
        await Expect(rows.Nth(1)).ToContainTextAsync("Clean");
        await Expect(rows.Nth(1)).ToContainTextAsync("Signed at the dock");
        await Expect(rows.Nth(1).GetByRole(AriaRole.Link, new() { Name = "cmr-early.pdf", Exact = true })).ToBeVisibleAsync();
        await Expect(rows.Nth(1).GetByRole(AriaRole.Link, new() { Name = "Download cmr-early.pdf" })).ToBeVisibleAsync();
        await Expect(proofs.Locator("del")).ToHaveCountAsync(0);

        await RecordProofAsync(reference, "M. Schulz", "cmr-signed.pdf");
        await Page.ReloadAsync();
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Nth(1).Locator("del")).ToHaveCountAsync(4);
        await Expect(rows.Nth(1)).ToContainTextAsync("J. Becker");
        await Expect(rows.Nth(2)).ToContainTextAsync("M. Schulz");
        await Expect(rows.Nth(2).Locator("del")).ToHaveCountAsync(0);
        await Expect(rows.Nth(1).GetByRole(AriaRole.Link, new() { Name = "cmr-early.pdf", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ADeliveredShipmentWithoutAProofOfDelivery_WaitsInTheFifthList_UntilItsProofOfDeliveryArrives()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-10));

        await OpenViewAsync("Delivered without Proof of delivery");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Delivered without Proof of delivery", Level = 1 })).ToBeVisibleAsync();
        var shipment = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(shipment).ToContainTextAsync("Van Dijk Transport");
        await Expect(shipment).Not.ToContainTextAsync("Missing");
        await Expect(shipment.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);

        await RecordProofAsync(reference, "J. Becker", "cmr.pdf");
        await Expect(shipment).ToHaveCountAsync(0);

        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Proofs of delivery" }).GetByRole(AriaRole.Link, new() { Name = "cmr.pdf", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AProofOfDeliveryFile_IsServedToThePlannerToViewOrDownload()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordProofAsync(reference, "J. Becker", "cmr-served.pdf");
        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var file = Page.GetByRole(AriaRole.Region, new() { Name = "Proofs of delivery" }).GetByRole(AriaRole.Link, new() { Name = "cmr-served.pdf", Exact = true });
        var viewPath = await file.GetAttributeAsync("href");

        var shown = await Page.APIRequest.GetAsync(viewPath!);
        shown.Status.Should().Be((int)HttpStatusCode.OK);
        shown.Headers["content-type"].Should().Be("application/pdf");
        (await shown.BodyAsync()).Should().StartWith("%PDF"u8.ToArray());

        var saved = await Page.APIRequest.GetAsync($"{viewPath}?download=true");
        saved.Headers["content-disposition"].Should().Contain("cmr-served.pdf");
    }

    private async Task RecordProofAsync(string reference, string receivedBy, string fileName)
    {
        var metadata = new JsonObject
        {
            ["documentId"] = Guid.NewGuid().ToString(),
            ["source"] = new JsonObject { ["type"] = "Carrier", ["carrierId"] = VanDijkId },
            ["carrierBookingReference"] = reference,
            ["receivedBy"] = receivedBy,
            ["deliveryTime"] = JsonValue.Create(DateTimeOffset.UtcNow.AddMinutes(-5)),
            ["location"] = new JsonObject { ["place"] = "Düsseldorf", ["postcode"] = "40213", ["countryCode"] = "DE" },
            ["condition"] = "Clean",
            ["remark"] = "Signed at the dock",
        };
        using var response = await _tms.RecordProofOfDeliveryAsync(metadata, fileName, [.. "%PDF-1.7 signed CMR "u8, .. Guid.NewGuid().ToByteArray()], TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}

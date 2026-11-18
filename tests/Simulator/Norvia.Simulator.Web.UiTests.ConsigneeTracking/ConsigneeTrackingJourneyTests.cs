using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.ConsigneeTracking;

/// <summary>
/// A Consignee opens the tracking link of a Transport Order the Simulator booked, outside the Entra gate and with nobody signed in (ADR 0052, #982). The Planner's
/// link is read from the Transport Order in the TMS UI, as the Planner hands it to the Shipper, and opened in a browser context of its own that holds nothing
/// of the Simulator or the Planner. The Simulator is the one that moves the Shipment, through the Carriers' real doors.
/// </summary>
public sealed partial class ConsigneeTrackingJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    // The Carriers' events reach the TMS through the tracking and Proof of delivery workers, a little after the run sends them.
    private const float PatienceMilliseconds = 120_000;

    // A stopped TMS makes the portal wait for its own timeout before it answers.
    private const float TmsDownPatienceMilliseconds = 90_000;

    private const float PlannerPagePatienceMilliseconds = 30_000;

    private static readonly string[] StatusOrder = ["Received", "Scheduled", "Collected", "In transit", "Delivered"];

    private readonly SimulatorUiFixture _simulator = simulator;

    [Fact]
    public async Task ATrackingLink_ShowsTheTrackingStatusAndProgress_AndNothingOnlyTheShipperMayKnow()
    {
        // Booked, planned, released and confirmed by the Carrier, which never tracks: the Shipment is Scheduled and nothing has happened to the goods.
        await StartRunAsync(tracked: 0, delivered: 0, proofOfDelivery: 0);
        var number = await TransportOrderNumberAsync();
        var code = await TrackingCodeAsync(number);

        await using var consignee = await NewConsigneeAsync();
        var response = await consignee.Page.GotoAsync(LinkOf(code));

        response!.Status.Should().Be(200);
        response.Headers["referrer-policy"].Should().Be("no-referrer");
        response.Headers["cache-control"].Should().Be("no-store");
        await Expect(consignee.Page.GetByRole(AriaRole.Heading, new() { Name = "Track your shipment", Level = 1 })).ToBeVisibleAsync();
        var shipment = consignee.Page.GetByLabel("Shipment", new() { Exact = true });
        await Expect(shipment).ToContainTextAsync("Scheduled", new() { Timeout = PatienceMilliseconds });
        await Expect(shipment).ToContainTextAsync("From");
        await Expect(consignee.Page.GetByText("Nothing has happened to the goods yet.")).ToBeVisibleAsync();
        await Expect(consignee.Page.GetByText("There is no Proof of delivery yet.")).ToBeVisibleAsync();
        await Expect(consignee.Page.GetByTestId("fetched-at")).ToContainTextAsync("Fetched at");

        // The Consignee's view has no number of Norvia's, no Shipper reference and no Carrier in it.
        var main = consignee.Page.GetByRole(AriaRole.Main);
        await Expect(main).Not.ToContainTextAsync(number);
        await Expect(main).Not.ToContainTextAsync(SimulatorReference());
        await Expect(main).Not.ToContainTextAsync(ShipmentNumber());
    }

    [Fact]
    public async Task AShipment_GoesFromItsFirstStatusToDelivered_OnTheLink_AsTheSimulatorMovesIt_AndTheLinkEndsWithTheProofOfDelivery()
    {
        // The link is opened as soon as the Transport Order exists, before the Carrier has tracked anything.
        await StartRunAsync(tracked: 100, delivered: 100, proofOfDelivery: 100);
        var code = await TrackingCodeAsync(await TransportOrderNumberAsync());
        await using var consignee = await NewConsigneeAsync();

        // The page is a plain one that looks again every minute, so the test looks again itself. The Carrier's events and the Proof of delivery reach the TMS through
        // workers, so the last of them can come after the first Delivered.
        var seen = new List<string>();
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(PatienceMilliseconds);
        var steps = 0;
        var proofShown = false;
        do
        {
            await consignee.Page.GotoAsync(LinkOf(code));
            var status = StatusOnPage().Match(await consignee.Page.GetByLabel("Shipment", new() { Exact = true }).InnerTextAsync()).Groups["status"].Value;
            status.Should().NotBeEmpty("the page shows a Status in its Shipment list");
            if (seen.LastOrDefault() != status)
            {
                seen.Add(status);
            }

            steps = await consignee.Page.GetByRole(AriaRole.List, new() { Name = "Progress" }).GetByRole(AriaRole.Listitem).CountAsync();
            proofShown = await consignee.Page.GetByLabel("Proof of delivery", new() { Exact = true }).CountAsync() > 0;
        }
        while ((seen.LastOrDefault() != "Delivered" || steps < 5 || !proofShown) && DateTimeOffset.UtcNow < deadline);

        // Whatever it showed on the way, it never went back, and it ended Delivered with every step and the Proof of delivery.
        seen.Should().EndWith("Delivered");
        seen.Select(status => Array.IndexOf(StatusOrder, status)).Should().BeInAscendingOrder().And.NotContain(-1);
        steps.Should().Be(5);
        var progress = consignee.Page.GetByRole(AriaRole.List, new() { Name = "Progress" });
        foreach (var step in new[] { "Collected", "Departed", "Arrived", "Out for delivery", "Delivered" })
        {
            await Expect(progress).ToContainTextAsync(step);
        }

        var proof = consignee.Page.GetByLabel("Proof of delivery", new() { Exact = true });
        await Expect(proof).ToContainTextAsync("Received by");
        await Expect(proof).ToContainTextAsync("Condition");
        // The Consignee sees who received it and how, and none of the files.
        await Expect(consignee.Page.GetByRole(AriaRole.Link)).ToHaveCountAsync(0);
        await Expect(consignee.Page.GetByRole(AriaRole.Img)).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("ZZZZZZZZZZZZZZZZ")]
    [InlineData("0")]
    public async Task AWrongCode_ShowsTheNotFoundPage_WhichSaysNothingOfWhetherTheCodeEverExisted(string wrong)
    {
        await using var consignee = await NewConsigneeAsync();

        var response = await consignee.Page.GotoAsync(LinkOf(wrong));

        response!.Status.Should().Be(404);
        var message = consignee.Page.GetByTestId("tracking-message");
        await Expect(message).ToHaveTextAsync("We can't find this shipment. Check the link, or contact the sender.");
        await Expect(consignee.Page.GetByLabel("Shipment", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(consignee.Page.GetByText("Fetched at")).ToHaveCountAsync(0);
        await Expect(consignee.Page.GetByRole(AriaRole.Main)).Not.ToContainTextAsync(new Regex("expired|replaced|revoked|invalid", RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task AReplacedCode_ShowsTheSamePageAsAWrongOne_WhileTheNewCodeOpensTheShipment()
    {
        await StartRunAsync(tracked: 0, delivered: 0, proofOfDelivery: 0);
        var number = await TransportOrderNumberAsync();
        var old = await TrackingCodeAsync(number);
        var replacement = await ReplaceTrackingCodeAsync(number);
        await using var consignee = await NewConsigneeAsync();

        await consignee.Page.GotoAsync(LinkOf(replacement));
        await Expect(consignee.Page.GetByLabel("Shipment", new() { Exact = true })).ToContainTextAsync("Scheduled");
        replacement.Should().NotBe(old);

        var response = await consignee.Page.GotoAsync(LinkOf(old));

        response!.Status.Should().Be(404);
        await Expect(consignee.Page.GetByTestId("tracking-message")).ToHaveTextAsync("We can't find this shipment. Check the link, or contact the sender.");
        var replacedPage = await consignee.Page.GetByRole(AriaRole.Main).InnerHTMLAsync();
        await consignee.Page.GotoAsync(LinkOf("ZZZZZZZZZZZZZZZZ"));
        (await consignee.Page.GetByRole(AriaRole.Main).InnerHTMLAsync()).Should().Be(replacedPage, "a replaced code and a wrong one are one answer");
    }

    [Fact]
    public async Task WithTheTmsDown_ThePageSaysTrackingIsUnavailable_NotThatTheShipmentIsUnknown_AndTheLinkWorksAgainWhenItIsUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await StartRunAsync(tracked: 0, delivered: 0, proofOfDelivery: 0);
        var code = await TrackingCodeAsync(await TransportOrderNumberAsync());
        await using var consignee = await NewConsigneeAsync();
        await consignee.Page.GotoAsync(LinkOf(code));
        await Expect(consignee.Page.GetByLabel("Shipment", new() { Exact = true })).ToContainTextAsync("Scheduled");

        await _simulator.StopTmsAsync(cancellationToken);
        try
        {
            // The page keeps no state between visits (it is a plain page behind no session), so it has no earlier answer to show beside the error: it shows only the error.
            var response = await consignee.Page.ReloadAsync(new() { Timeout = TmsDownPatienceMilliseconds });

            response!.Status.Should().Be(503);
            await Expect(consignee.Page.GetByTestId("tracking-message")).ToHaveTextAsync("Tracking is temporarily unavailable. Try again shortly.");
            await Expect(consignee.Page.GetByLabel("Shipment", new() { Exact = true })).ToHaveCountAsync(0);
            await Expect(consignee.Page.GetByRole(AriaRole.Main)).Not.ToContainTextAsync("We can't find this shipment");
        }
        finally
        {
            await _simulator.StartTmsAsync(cancellationToken);
        }

        var recovered = await consignee.Page.ReloadAsync();
        recovered!.Status.Should().Be(200);
        await Expect(consignee.Page.GetByLabel("Shipment", new() { Exact = true })).ToContainTextAsync("Scheduled");
    }

    private string LinkOf(string code) => new Uri(_simulator.PartnerPortal, $"/track/{code}").ToString();

    // The Consignee has no sign-in and no session: a context of its own, with nothing of the Planner's or the Simulator's.
    private async Task<ConsigneePage> NewConsigneeAsync() => await ConsigneePage.OpenAsync(_simulator);

    private async Task StartRunAsync(int tracked, int delivered, int proofOfDelivery)
    {
        // A seed of this test's own, so its plan and its references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");
        await Page.GetByLabel("Orders").FillAsync("1");
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 0), ("Reassigned (%)", 0),
            ("Released by the Planner (%)", 100), ("Confirmed by the Carrier (%)", 100), ("Declined by the Carrier (%)", 0), ("Tracked (%)", tracked),
            ("Exceptions on the way (%)", 0), ("Delivered (%)", delivered), ("With a Proof of delivery (%)", proofOfDelivery), ("Failed collections cancelled (%)", 0),
        })
        {
            await Page.GetByLabel(label).FillAsync(percent.ToString(CultureInfo.InvariantCulture));
        }

        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
    }

    // The number of the Transport Order the run booked, from the line that assigns it.
    private async Task<string> TransportOrderNumberAsync()
    {
        var assignment = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }).GetByRole(AriaRole.Row)
            .Filter(new() { HasText = "Assign Transport Order" }).Filter(new() { HasText = "Planned" });
        await Expect(assignment).ToHaveCountAsync(1, new() { Timeout = PatienceMilliseconds });
        return TransportOrderNumber().Match(await assignment.InnerTextAsync()).Value;
    }

    // The Planner opens the Transport Order in the TMS UI, where the link it hands the Shipper is.
    private async Task<(IPage Planner, ILocator Link)> OpenPlannerPageAsync(string number)
    {
        var planner = await Page.Context.NewPageAsync();
        await planner.GotoAsync(new Uri(_simulator.TmsUi, $"/transport-orders/{number}").ToString());
        var link = planner.GetByTestId("tracking-link");
        await Expect(link).ToBeVisibleAsync(new() { Timeout = PlannerPagePatienceMilliseconds });
        return (planner, link);
    }

    private async Task<string> TrackingCodeAsync(string number)
    {
        var (planner, link) = await OpenPlannerPageAsync(number);
        await using var _ = planner;
        var code = TrackingCode().Match(await link.InnerTextAsync()).Groups["code"].Value;
        code.Should().HaveLength(16);
        return code;
    }

    private async Task<string> ReplaceTrackingCodeAsync(string number)
    {
        var (planner, link) = await OpenPlannerPageAsync(number);
        await using var _ = planner;
        var before = await link.InnerTextAsync();
        await planner.GetByTestId("replace-tracking-code").ClickAsync();
        await Expect(link).Not.ToHaveTextAsync(before);
        return TrackingCode().Match(await link.InnerTextAsync()).Groups["code"].Value;
    }

    [GeneratedRegex(@"TO-\d{7}")]
    private static partial Regex TransportOrderNumber();

    [GeneratedRegex(@"SH-\d{7}")]
    private static partial Regex ShipmentNumber();

    [GeneratedRegex(@"SIM-\d+-\d+")]
    private static partial Regex SimulatorReference();

    [GeneratedRegex(@"/track/(?<code>[0-9A-Z]+)")]
    private static partial Regex TrackingCode();

    [GeneratedRegex(@"Status\s+(?<status>[^\r\n]+)")]
    private static partial Regex StatusOnPage();

}

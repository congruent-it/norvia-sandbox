using AngleSharp.Dom;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class TrackingTimelineTests : TmsWebContext
{
    [Fact]
    public void Render_AShipmentWithTrackingEvents_ShowsTheTimelineInTheOrderTheQueryGives()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline(
        [
            Entry(TrackingEventType.Collected, new DateTimeOffset(2026, 9, 26, 6, 0, 0, TimeSpan.Zero), moved: true),
            Entry(TrackingEventType.Damaged, new DateTimeOffset(2026, 9, 26, 7, 0, 0, TimeSpan.Zero), moved: false) with { Remark = "Pallet dented" },
        ], [], [], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => Rows(page).Should().Equal(
            "Collected | Sat 26 Sep 2026, 08:00 | Rotterdam, 3011 AA, NL | Sat 26 Sep 2026, 08:05 | Yes | Not given | ",
            "Damaged | Sat 26 Sep 2026, 09:00 | Rotterdam, 3011 AA, NL | Sat 26 Sep 2026, 09:05 | No | Not given | Pallet dented"));
    }

    [Fact]
    public void Render_AShipmentNoCarrierHasReportedOn_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => Section(page).TextContent.Should().Contain("No Tracking events have been reported for this Shipment."));
        Section(page).QuerySelectorAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheTimelineCantBeRead_ShowsAnErrorInItsSection()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns<ShipmentTimeline?>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => Section(page).TextContent.Should().Contain("The Tracking timeline couldn't be loaded."));
    }

    [Fact]
    public void Render_AShipmentWithProofsOfDelivery_ShowsEachWithItsDetailsAndFiles()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([],
        [
            Proof(current: true) with
            {
                Condition = DeliveryCondition.Reservations,
                Remark = "Two cartons crushed",
                Files = [new ProofOfDeliveryFileView(FileId, "cmr.pdf", "application/pdf", 48 * 1024)],
            },
        ], [], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => ProofRows(page).Should().Equal(
            "J. de Vries | Sat 26 Sep 2026, 12:00 | With reservations | Two cartons crushed | cmr.pdf (download, 48 KB)"));
        page.Find($"a[href='/shipments/SH-0000012/proof-of-delivery/files/{FileId}']").GetAttribute("target").Should().Be("_blank");
        var download = page.Find($"a[href='/shipments/SH-0000012/proof-of-delivery/files/{FileId}?download=true']");
        download.GetAttribute("download").Should().Be("cmr.pdf");
        download.GetAttribute("aria-label").Should().Be("Download cmr.pdf");
        page.FindAll("del").Should().BeEmpty();
    }

    [Fact]
    public void Render_ASupersededProofOfDelivery_IsStruckThroughAndKeptBesideTheCurrentOne()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([],
        [
            Proof(current: false) with { ReceivedBy = "A. Jansen" },
            Proof(current: true),
        ], [], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(2));
        var rows = page.FindAll("tbody tr");
        rows[0].QuerySelectorAll("del").Select(struck => struck.TextContent).Should().Contain("A. Jansen");
        rows[1].QuerySelectorAll("del").Should().BeEmpty();
    }

    [Fact]
    public void Render_AShipmentWithNoProofOfDelivery_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([Entry(TrackingEventType.Collected, new DateTimeOffset(2026, 9, 26, 6, 0, 0, TimeSpan.Zero), moved: true)], [], [], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => page.Find("section[aria-labelledby='section-proofs-of-delivery']").TextContent.Should().Contain("No Proof of delivery has been received for this Shipment."));
    }

    [Fact]
    public void Render_AShipmentWithNoExceptionOrAgreedWindow_SaysSoInEachSection()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() =>
        {
            page.Find("section[aria-labelledby='section-exceptions']").TextContent.Should().Contain("No Exception has been opened for this Shipment.");
            page.Find("section[aria-labelledby='section-agreed-delivery-windows']").TextContent.Should().Contain("No delivery window has been agreed for this Shipment");
        });
    }

    [Fact]
    public void Render_AnExceptionOnTheTimeline_ShowsItsMarkersCausesNotesNotificationsAndOutcome()
    {
        var opened = new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero);
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [],
        [
            new ExceptionTimelineEntry(
                "EX-0000005", ExceptionType.DeliveryFailed, ExceptionStatus.Resolved, opened, true, true,
                [new ExceptionCauseView(ExceptionCauseKind.TrackingEvent, "evt-ndl", opened)],
                [new ExceptionNoteView("Phoned Kessler", "Anna de Vries", opened.AddHours(1))],
                [new ShipperNotificationView("Your delivery failed.", "Anna de Vries", ShipperNotificationStatus.Sent, opened.AddHours(2), opened.AddHours(2), null)],
                [],
                ExceptionOutcome.Redelivered, "Delivered Thursday", opened.AddHours(3)),
            new ExceptionTimelineEntry("EX-0000006", ExceptionType.LateDelivery, ExceptionStatus.Open, opened, false, false, [], [], [], [], null, null, null),
        ], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() =>
        {
            var resolved = page.Find("article[aria-label='Exception EX-0000005']");
            resolved.QuerySelector("h3")!.TextContent.Should().Contain("EX-0000005").And.Contain("Delivery failed").And.Contain("Resolved")
                .And.Contain("Cause cleared").And.Contain("After Delivered");
            resolved.QuerySelectorAll("tbody tr").Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()))).Should().Equal(
                "Thu 24 Sep 2026, 11:45 | Cause: Tracking event | Reported as evt-ndl",
                "Thu 24 Sep 2026, 12:45 | Note by Anna de Vries | Phoned Kessler",
                "Thu 24 Sep 2026, 13:45 | Shipper notification sent (by Anna de Vries) | Your delivery failed.",
                "Thu 24 Sep 2026, 14:45 | Resolved: Redelivered | Delivered Thursday");
            page.Find("article[aria-label='Exception EX-0000006'] h3").TextContent.Should().Contain("Open").And.NotContain("Cause cleared");
        });
    }

    [Fact]
    public void Render_TheAgreedDeliveryWindows_ShowEachWithTheLastCounting()
    {
        var agreed = new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero);
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [],
        [
            new AgreedDeliveryWindowView(agreed.AddDays(1), agreed.AddDays(1).AddHours(4), "EX-0000005", agreed),
            new AgreedDeliveryWindowView(agreed.AddDays(2), agreed.AddDays(2).AddHours(4), "EX-0000005", agreed.AddHours(1)),
        ]));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => page.Find("section[aria-labelledby='section-agreed-delivery-windows']").QuerySelectorAll("tbody tr")
            .Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()))).Should().Equal(
                "Fri 25 Sep 2026, 11:45–15:45 | Thu 24 Sep 2026, 11:45 | EX-0000005 | ",
                "Sat 26 Sep 2026, 11:45–15:45 | Thu 24 Sep 2026, 12:45 | EX-0000005 | Current"));
    }

    private static readonly Guid FileId = Guid.Parse("6f1c2f55-4a3f-4a38-9a54-2b7a1f0f7a11");

    // Signed by J. de Vries at 12:00 on 26 September 2026 in Rotterdam (UTC+2).
    private static ProofOfDeliveryView Proof(bool current) => new(
        Guid.NewGuid(), "SH-0000012", "CAR-0002", Guid.NewGuid().ToString(), "J. de Vries", new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero),
        "Rotterdam", "3011 AA", "NL", DeliveryCondition.Clean, null, new DateTimeOffset(2026, 9, 26, 10, 5, 0, TimeSpan.Zero), "corr-1", [], current);

    private static IEnumerable<string> ProofRows(IRenderedComponent<TrackingTimeline> page) =>
        page.Find("section[aria-labelledby='section-proofs-of-delivery']").QuerySelectorAll("tbody tr")
            .Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => System.Text.RegularExpressions.Regex.Replace(cell.TextContent.Trim(), @"\s+", " "))));

    // Reported at 08:00 and 09:00 in Rotterdam (UTC+2), received five minutes later.
    private static TrackingTimelineEntry Entry(TrackingEventType type, DateTimeOffset eventTime, bool moved) =>
        new(type, eventTime, "Rotterdam", "3011 AA", "NL", null, null, eventTime.AddMinutes(5), moved);

    [Fact]
    public void Render_ABillableOrder_ShowsTheBillingStateAndWhenItBecameBillable()
    {
        var billable = new ShipmentBillingView(BillingState.Billable, new DateTimeOffset(2026, 9, 25, 8, 15, 0, TimeSpan.Zero), null);
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [], [], billable));

        var page = RenderTimeline("SH-0000012");

        var billing = page.WaitForElement("section[aria-labelledby='section-billing']");
        DescriptionsOf(billing).Should().Equal(("Billing state", "Billable"), ("Became Billable", "Fri 25 Sep 2026, 10:15"));
    }

    [Fact]
    public void Render_ANotYetBillableOrder_SaysItIsNotBillableYet_WithoutAReleaseOrABillableTime()
    {
        var notYet = new ShipmentBillingView(BillingState.NotYetBillable, null, null);
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [], [], notYet));

        var page = RenderTimeline("SH-0000012");

        DescriptionsOf(page.WaitForElement("section[aria-labelledby='section-billing']")).Should().Equal(("Billing state", "Not yet billable"));
    }

    [Fact]
    public void Render_AFulfilmentOrder_SaysItIsBilledThroughTheFulfilmentContract()
    {
        var fulfilment = new ShipmentBillingView(BillingState.NotBilledSeparately, null, null);
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [], [], fulfilment));

        var page = RenderTimeline("SH-0000012");

        DescriptionsOf(page.WaitForElement("section[aria-labelledby='section-billing']")).Should().Equal(("Billing state", "Not billed separately (Fulfilment Contract)"));
    }

    [Fact]
    public void Render_AReleaseWithoutProofOfDelivery_ShowsWhoReleasedItWhenAndWhy()
    {
        var released = new ShipmentBillingView(
            BillingState.Billable, new DateTimeOffset(2026, 9, 25, 8, 15, 0, TimeSpan.Zero),
            new ProofOfDeliveryReleaseView("Sanne Bakker", new DateTimeOffset(2026, 9, 25, 8, 15, 0, TimeSpan.Zero), "CMR received by post"));
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [], [], released));

        var page = RenderTimeline("SH-0000012");

        DescriptionsOf(page.WaitForElement("section[aria-labelledby='section-billing']")).Should().Equal(
            ("Billing state", "Billable"),
            ("Became Billable", "Fri 25 Sep 2026, 10:15"),
            ("Released without Proof of delivery", "Fri 25 Sep 2026, 10:15 by Sanne Bakker"),
            ("Remark", "CMR received by post"));
    }

    [Fact]
    public void Render_AShipmentWithoutABillingView_ShowsNoBillingSection()
    {
        Queries.FindTrackingTimelineAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ShipmentTimeline([], [], [], []));

        var page = RenderTimeline("SH-0000012");

        page.WaitForAssertion(() => page.Find("section[aria-labelledby='section-tracking-timeline']"));
        page.FindAll("section[aria-labelledby='section-billing']").Should().BeEmpty();
    }

    private static List<(string Term, string Description)> DescriptionsOf(IElement section) =>
        [.. section.QuerySelectorAll("dt").Zip(section.QuerySelectorAll("dd"), (term, description) => (term.TextContent.Trim(), description.TextContent.Trim()))];

    private IRenderedComponent<TrackingTimeline> RenderTimeline(string number) =>
        Render<TrackingTimeline>(parameters => parameters.Add(page => page.ShipmentNumber, number));

    private static IElement Section(IRenderedComponent<TrackingTimeline> page) => page.Find("section[aria-labelledby='section-tracking-timeline']");

    // Each row of the table as its cells joined with " | ".
    private static IEnumerable<string> Rows(IRenderedComponent<TrackingTimeline> page) =>
        Section(page).QuerySelectorAll("tbody tr").Select(row => string.Join(" | ", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));
}

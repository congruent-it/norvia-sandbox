using AngleSharp.Dom;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class BookingsAwaitingAnswerTests : TmsWebContext
{
    [Fact]
    public void Render_BeforeTheBookingsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>>();
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<BookingsAwaitingAnswer>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the bookings awaiting an answer");
    }

    [Fact]
    public void Render_WithRequestedBookings_ListsEachWithItsShipmentCarrierNumberAndTimes()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns([Booking("CB-0000007", "SH-0000012")]);

        var page = Render<BookingsAwaitingAnswer>();

        var row = page.WaitForElement("tr:has(a[href='/shipments/SH-0000012'])");
        var cells = row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        cells[0].Should().Be("SH-0000012");
        cells[1].Should().Be("Kessler Spedition");
        cells[2].Should().Be("CB-0000007");
        cells[3].Should().Be("Sat 26 Sep 2026, 06:00");
        cells[4].Should().Be("Thu 24 Sep 2026, 11:45");
        cells[5].Should().Be("Not yet sent");
        cells[6].Should().BeEmpty();
    }

    [Fact]
    public void Render_ABookingTheCarrierAcknowledged_ShowsWhenItWasSent()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>())
            .Returns([Booking("CB-0000007", "SH-0000012") with { SentAt = new DateTimeOffset(2026, 9, 24, 9, 46, 0, TimeSpan.Zero) }]);

        var page = Render<BookingsAwaitingAnswer>();

        page.WaitForAssertion(() => CellsOf(page, "SH-0000012")[5].Should().Be("Thu 24 Sep 2026, 11:46"));
    }

    [Fact]
    public void Render_AnOverdueBooking_FlagsItOverdue()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(
            [Booking("CB-0000007", "SH-0000012") with { Overdue = true }, Booking("CB-0000008", "SH-0000013")]);

        var page = Render<BookingsAwaitingAnswer>();

        page.WaitForAssertion(() =>
        {
            CellsOf(page, "SH-0000012")[6].Should().Be("Overdue");
            CellsOf(page, "SH-0000013")[6].Should().BeEmpty();
        });
    }

    [Fact]
    public void Render_WithRequestedBookings_ListsThemInTheOrderTheQueryGives()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>())
            .Returns([Booking("CB-0000008", "SH-0000013"), Booking("CB-0000007", "SH-0000012")]);

        var page = Render<BookingsAwaitingAnswer>();

        page.WaitForAssertion(() => page.FindAll("a[href^='/shipments/']").Select(link => link.TextContent)
            .Should().Equal("SH-0000013", "SH-0000012"));
    }

    [Fact]
    public void Render_WithNoBookingsAwaitingAnAnswer_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<BookingsAwaitingAnswer>();

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("No Carrier bookings are waiting for an answer."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenABookingIsRequestedWhileTheListIsOpen_ShowsItWithoutARefresh()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(
            [Booking("CB-0000007", "SH-0000012")],
            [Booking("CB-0000007", "SH-0000012"), Booking("CB-0000008", "SH-0000013")]);
        var page = Render<BookingsAwaitingAnswer>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/shipments/SH-0000013']");
    }

    [Fact]
    public void Render_WhenABookingIsAnsweredWhileTheListIsOpen_DropsItWithoutARefresh()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(
            [Booking("CB-0000007", "SH-0000012"), Booking("CB-0000008", "SH-0000013")],
            [Booking("CB-0000008", "SH-0000013")]);
        var page = Render<BookingsAwaitingAnswer>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("a[href='/shipments/SH-0000012']").Should().BeEmpty());
        page.Find("a[href='/shipments/SH-0000013']");
    }

    [Fact]
    public void Render_WhenTheBookingsCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>>([Booking("CB-0000007", "SH-0000012")]));

        var page = Render<BookingsAwaitingAnswer>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The bookings awaiting an answer couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Withdraw_Withdrawn_SaysSoForThatBookingOnly()
    {
        Commands.WithdrawAsync("CB-0000007", Arg.Any<CancellationToken>()).Returns(new WithdrawAnswer.Withdrawn());
        var page = RenderWith(Booking("CB-0000007", "SH-0000012"), Booking("CB-0000008", "SH-0000013"));

        WithdrawButton(page, "CB-0000007").Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(("status", "Carrier booking CB-0000007 is withdrawn. Its Shipment is back in Shipments to release.")));
        Commands.DidNotReceive().WithdrawAsync("CB-0000008", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Withdraw_Refused_ShowsTheReadableReason_AndKeepsTheRow()
    {
        Commands.WithdrawAsync("CB-0000007", Arg.Any<CancellationToken>())
            .Returns(new WithdrawAnswer.Refused("Carrier booking CB-0000007 is Confirmed, so it can't be withdrawn."));
        var page = RenderWith(Booking("CB-0000007", "SH-0000012"));

        WithdrawButton(page, "CB-0000007").Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            ("alert", "Carrier booking CB-0000007 wasn't withdrawn: Carrier booking CB-0000007 is Confirmed, so it can't be withdrawn.")));
        page.Find("a[href='/shipments/SH-0000012']");
    }

    [Fact]
    public void Withdraw_WhenTheTmsApiCantBeReached_SaysToTryAgain()
    {
        Commands.WithdrawAsync("CB-0000007", Arg.Any<CancellationToken>()).Returns<WithdrawAnswer>(_ => throw new HttpRequestException("Connection refused."));
        var page = RenderWith(Booking("CB-0000007", "SH-0000012"));

        WithdrawButton(page, "CB-0000007").Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(("alert", "Carrier booking CB-0000007 couldn't be withdrawn. Try again.")));
        WithdrawButton(page, "CB-0000007").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Withdraw_AgainAfterARefusal_ReplacesTheEarlierOutcome()
    {
        Commands.WithdrawAsync("CB-0000007", Arg.Any<CancellationToken>()).Returns(
            new WithdrawAnswer.Refused("Not open."),
            new WithdrawAnswer.Withdrawn());
        var page = RenderWith(Booking("CB-0000007", "SH-0000012"));
        WithdrawButton(page, "CB-0000007").Click();
        page.WaitForAssertion(() => Outcomes(page).Should().ContainSingle().Which.Role.Should().Be("alert"));

        WithdrawButton(page, "CB-0000007").Click();

        page.WaitForAssertion(() => Outcomes(page).Should().ContainSingle().Which.Role.Should().Be("status"));
    }

    private IRenderedComponent<BookingsAwaitingAnswer> RenderWith(params CarrierBookingAwaitingAnswerSummary[] bookings)
    {
        Queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(bookings);
        var page = Render<BookingsAwaitingAnswer>();
        page.WaitForElement($"a[href='/shipments/{bookings[0].ShipmentNumber}']");
        return page;
    }

    // Requested 11:45 on 24 September 2026 in Rotterdam (UTC+2), to collect from 06:00 on 26 September, not yet sent.
    private static CarrierBookingAwaitingAnswerSummary Booking(string number, string shipmentNumber) => new(
        shipmentNumber,
        number,
        Samples.Kessler,
        new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero),
        null,
        new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero),
        false);

    private static List<string> CellsOf(IRenderedComponent<BookingsAwaitingAnswer> page, string shipmentNumber) =>
        [.. page.Find($"tr:has(a[href='/shipments/{shipmentNumber}'])").QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())];

    private static IElement WithdrawButton(IRenderedComponent<BookingsAwaitingAnswer> page, string number) =>
        page.Find($"fluent-button[aria-label='Withdraw {number}']");

    // Each withdrawal outcome's role and message, as it reads without its icon.
    private static List<(string Role, string Text)> Outcomes(IRenderedComponent<BookingsAwaitingAnswer> page) =>
        [.. page.FindAll("ul[aria-label='Withdrawal outcomes'] li").Select(item => (
            item.GetAttribute("role")!,
            string.Join(' ', item.QuerySelector(".fluent-messagebar-message")!.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))))];
}

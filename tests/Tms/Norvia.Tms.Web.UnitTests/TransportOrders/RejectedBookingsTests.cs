using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class RejectedBookingsTests : TmsWebContext
{
    [Fact]
    public void Render_BeforeTheRejectedBookingsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<TransportOrderSummary>>();
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>())
            .Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<RejectedBookings>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the rejected bookings");
    }

    [Fact]
    public void Render_WithRejectedBookings_ListsEachWithShipperShipperReferenceAndWhenItWasReceivedInRotterdamTime()
    {
        var booking = Samples.RejectedSummary("BD-2026-0099");
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>()).Returns([booking]);

        var page = Render<RejectedBookings>();

        var row = page.WaitForElement($"tr:has(a[href='/rejected-bookings/{booking.Id}'])");
        var cells = row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim());
        cells.Should().Equal("Thu 24 Sep 2026, 11:30", "Brouwer Dranken", "BD-2026-0099");
    }

    [Fact]
    public void Render_ARejectedBookingWithoutAShipperReference_SaysItWasNotGiven()
    {
        var booking = Samples.RejectedSummary(reference: null);
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>()).Returns([booking]);

        var page = Render<RejectedBookings>();

        var row = page.WaitForElement($"tr:has(a[href='/rejected-bookings/{booking.Id}'])");
        row.QuerySelectorAll("td")[^1].TextContent.Trim().Should().Be("Not given");
    }

    [Fact]
    public void Render_WithSeveralRejectedBookings_ListsTheLatestFirst()
    {
        var earlier = Samples.RejectedSummary("BD-2026-0098");
        var later = Samples.RejectedSummary("BD-2026-0099") with { ReceivedAt = earlier.ReceivedAt.AddHours(1) };
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>()).Returns([earlier, later]);

        var page = Render<RejectedBookings>();

        page.WaitForAssertion(() => page.FindAll("tbody tr")
            .Select(row => row.QuerySelectorAll("td")[^1].TextContent.Trim())
            .Should().Equal("BD-2026-0099", "BD-2026-0098"));
    }

    [Fact]
    public void Render_WithNoRejectedBookings_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<RejectedBookings>();

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("No bookings have been rejected."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenABookingIsRejectedWhileTheListIsOpen_ShowsItWithoutARefresh()
    {
        var first = Samples.RejectedSummary("BD-2026-0098");
        var second = Samples.RejectedSummary("BD-2026-0099");
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>()).Returns([first], [first, second]);
        var page = Render<RejectedBookings>();
        page.WaitForElement($"a[href='/rejected-bookings/{first.Id}']");

        Time.Advance(RefreshInterval);

        page.WaitForElement($"a[href='/rejected-bookings/{second.Id}']");
    }

    [Fact]
    public void Render_WhenTheRejectedBookingsCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        var booking = Samples.RejectedSummary();
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<TransportOrderSummary>>([booking]));

        var page = Render<RejectedBookings>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The rejected bookings couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement($"a[href='/rejected-bookings/{booking.Id}']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsTheRejectedBookingsAlreadyShownAndWarnsTheyMayBeOutOfDate()
    {
        var booking = Samples.RejectedSummary();
        Queries.ListRejectedAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<IReadOnlyList<TransportOrderSummary>>([booking]),
            _ => throw new TimeoutException("The database didn't answer."));
        var page = Render<RejectedBookings>();
        page.WaitForElement($"a[href='/rejected-bookings/{booking.Id}']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The list couldn't be refreshed and may be out of date. Trying again…"));
        page.Find($"a[href='/rejected-bookings/{booking.Id}']");
    }
}

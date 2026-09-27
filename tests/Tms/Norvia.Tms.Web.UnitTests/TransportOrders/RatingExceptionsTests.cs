using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.RatingExceptions;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class RatingExceptionsTests : TmsWebContext
{
    [Fact]
    public void Render_BeforeTheRatingExceptionsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<RatingExceptionSummary>>();
        Queries.ListRatingExceptionsAsync(Arg.Any<CancellationToken>())
            .Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<RatingExceptions>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the rating exceptions");
    }

    [Fact]
    public void Render_WithNoRatingExceptions_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListRatingExceptionsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<RatingExceptions>();

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("No Transport Order has Rating failed."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheRatingExceptionsCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        var exception = Samples.RatingException();
        Queries.ListRatingExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<RatingExceptionSummary>>([exception]));

        var page = Render<RatingExceptions>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The rating exceptions couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement($"a[href='/transport-orders/{exception.TransportOrderNumber}']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsTheRatingExceptionsAlreadyShownAndWarnsTheyMayBeOutOfDate()
    {
        var exception = Samples.RatingException();
        Queries.ListRatingExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<IReadOnlyList<RatingExceptionSummary>>([exception]),
            _ => throw new TimeoutException("The database didn't answer."));
        var page = Render<RatingExceptions>();
        page.WaitForElement($"a[href='/transport-orders/{exception.TransportOrderNumber}']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The list couldn't be refreshed and may be out of date. Trying again…"));
        page.Find($"a[href='/transport-orders/{exception.TransportOrderNumber}']");
    }

    [Fact]
    public void Render_WhenAnOrderIsReRatedWhileTheListIsOpen_DropsItWithoutARefresh()
    {
        var rated = Samples.RatingException("TO-0000043");
        var stillFailing = Samples.RatingException("TO-0000044");
        Queries.ListRatingExceptionsAsync(Arg.Any<CancellationToken>()).Returns([rated, stillFailing], [stillFailing]);
        var page = Render<RatingExceptions>();
        page.WaitForElement("a[href='/transport-orders/TO-0000043']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("a[href='/transport-orders/TO-0000043']").Should().BeEmpty());
        page.Find("a[href='/transport-orders/TO-0000044']");
    }

    [Fact]
    public void Render_WithRatingExceptions_ListsEachWithShipperFailureCodeReasonAndWhenRatingFailed()
    {
        var exception = Samples.RatingException("TO-0000043", "BD-2026-0043");
        Queries.ListRatingExceptionsAsync(Arg.Any<CancellationToken>()).Returns([exception]);

        var page = Render<RatingExceptions>();

        var row = page.WaitForElement("tr:has(a[href='/transport-orders/TO-0000043'])");
        var cells = row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        cells[..6].Should().Equal(
            "TO-0000043",
            "Brouwer Dranken",
            "BD-2026-0043",
            "NoMatchingLane",
            "Rate Card RC-0001 version 1 has no Lane from Rotterdam to Düsseldorf.",
            "Thu 24 Sep 2026, 11:31");
    }

    [Fact]
    public void ReRate_ThatRatesTheOrder_ReRatesThatOrderAndSaysItIsNowRated()
    {
        var page = RenderWith(Samples.RatingException("TO-0000043"), Samples.RatingException("TO-0000044"));
        Commands.ReRateAsync("TO-0000043", Arg.Any<CancellationToken>()).Returns(new ReRateAnswer.Rated());

        ReRateButton(page, "TO-0000043").Click();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should()
            .Contain("Transport Order TO-0000043 is now Rated."));
        Commands.DidNotReceive().ReRateAsync("TO-0000044", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReRate_ThatFailsAgain_SaysSoWithTheNewReason()
    {
        var page = RenderWith(Samples.RatingException("TO-0000043"));
        Commands.ReRateAsync("TO-0000043", Arg.Any<CancellationToken>())
            .Returns(new ReRateAnswer.FailedAgain(new RatingFailureView("WeightAboveHighestBreak", "The Lane prices up to 5,000 kg only.")));

        ReRateButton(page, "TO-0000043").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Rating failed again for Transport Order TO-0000043: The Lane prices up to 5,000 kg only."));
    }

    [Fact]
    public void ReRate_ThatTheTmsRefuses_SaysWhy()
    {
        var page = RenderWith(Samples.RatingException("TO-0000043"));
        Commands.ReRateAsync("TO-0000043", Arg.Any<CancellationToken>())
            .Returns(new ReRateAnswer.Refused("Transport Order TO-0000043 is Rated; only a Transport Order whose Rating failed can be re-rated."));

        ReRateButton(page, "TO-0000043").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Transport Order TO-0000043 wasn't re-rated: Transport Order TO-0000043 is Rated; only a Transport Order whose Rating failed can be re-rated."));
    }

    [Fact]
    public void ReRate_WhenTheTmsCantBeReached_ShowsAnErrorToTryAgain()
    {
        var page = RenderWith(Samples.RatingException("TO-0000043"));
        Commands.ReRateAsync("TO-0000043", Arg.Any<CancellationToken>())
            .Returns<ReRateAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));

        ReRateButton(page, "TO-0000043").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Transport Order TO-0000043 couldn't be re-rated. Try again."));
        ReRateButton(page, "TO-0000043").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ReRate_WhileTheTmsIsAnswering_CantBeSentAgain()
    {
        var page = RenderWith(Samples.RatingException("TO-0000043"));
        var answer = new TaskCompletionSource<ReRateAnswer>();
        Commands.ReRateAsync("TO-0000043", Arg.Any<CancellationToken>()).Returns(answer.Task);

        ReRateButton(page, "TO-0000043").Click();

        page.WaitForAssertion(() => ReRateButton(page, "TO-0000043").HasAttribute("disabled").Should().BeTrue());
        answer.SetResult(new ReRateAnswer.Rated());
        page.WaitForAssertion(() => ReRateButton(page, "TO-0000043").HasAttribute("disabled").Should().BeFalse());
    }

    private IRenderedComponent<RatingExceptions> RenderWith(params RatingExceptionSummary[] exceptions)
    {
        Queries.ListRatingExceptionsAsync(Arg.Any<CancellationToken>()).Returns(exceptions);
        var page = Render<RatingExceptions>();
        page.WaitForElement($"a[href='/transport-orders/{exceptions[0].TransportOrderNumber}']");
        return page;
    }

    private static AngleSharp.Dom.IElement ReRateButton(IRenderedComponent<RatingExceptions> page, string number) =>
        page.Find($"fluent-button[aria-label='Re-rate {number}']");
}

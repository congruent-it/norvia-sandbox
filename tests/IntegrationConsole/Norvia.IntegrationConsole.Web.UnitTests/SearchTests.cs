using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Search;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Trails;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class SearchTests : ConsoleWebContext
{
    // 12:00 in Rotterdam (CEST).
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly ITrailRequests _trails = Substitute.For<ITrailRequests>();

    public SearchTests()
    {
        Services.AddSingleton(_trails);
        Services.AddSingleton(Options.Create(new IntegrationRetention { IntegrationRetentionDays = 90 }));
        Answer();
    }

    [Fact]
    public void Render_ForAnOperator_ShowsTheSearchPage()
    {
        var page = Render<Search>();

        page.Find("h1").TextContent.Should().Be("Search Message trails");
    }

    [Fact]
    public void Authorize_ForAnyOperator_RequiresTheReadRole()
    {
        // Message trails lead to message bodies, which hold personal data.
        typeof(Search).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }

    [Fact]
    public void Render_WithoutAKey_OffersTheSearchBox_AndSearchesNothing()
    {
        var page = Render<Search>();

        page.FindAll("label").Select(label => label.TextContent.Trim()).Should().Contain(["Correlation ID or business key", "Time range"]);
        _trails.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Render_WithAKeyInTheAddress_SearchesTheLast24HoursForIt()
    {
        RenderAt("/?key=TO-2026-000123");

        _trails.Received(1).SearchAsync(new SearchTrailsQuery("TO-2026-000123", TimeSpan.FromHours(24)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("1h", 1)]
    [InlineData("24h", 24)]
    [InlineData("7d", 7 * 24)]
    [InlineData("30d", 30 * 24)]
    public void Render_WithATimeRangeInTheAddress_SearchesThatRange(string range, int hours)
    {
        RenderAt($"/?key=TO-2026-000123&within={range}");

        _trails.Received(1).SearchAsync(new SearchTrailsQuery("TO-2026-000123", TimeSpan.FromHours(hours)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_WithTheWholeRetentionPeriod_SearchesAsFarBackAsTheEstateKeeps()
    {
        var page = RenderAt("/?key=TO-2026-000123&within=all");

        _trails.Received(1).SearchAsync(Arg.Is<SearchTrailsQuery>(query => query.Within >= TimeSpan.FromDays(90)), Arg.Any<CancellationToken>());
        page.FindAll("fluent-option").Select(option => option.TextContent.Trim()).Should().Contain("Everything kept (90 days)");
    }

    [Fact]
    public void Search_FromTheBox_PutsTheKeyAndTheTimeRangeInTheAddress()
    {
        // So a search can be bookmarked, sent to a colleague, and linked to from a business key.
        var page = Render<Search>();

        page.Find("fluent-text-field").Change("  TO-2026-000123 ");
        page.Find("form").Submit();

        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/?key=TO-2026-000123&within=24h");
    }

    [Fact]
    public void Render_WithTrailsFound_ShowsOneRowPerTrail_WithItsKeys()
    {
        Answer(
            new TrailSummary(
                "corr-1",
                ["transport-order-intake"],
                Now.AddMinutes(-5),
                Now.AddMinutes(-4),
                Outcome.Succeeded,
                [new(BusinessKeyKind.TransportOrderNumber, "TO-2026-000123"), new(BusinessKeyKind.SourceMessageId, "evt-5c1d")]),
            new TrailSummary("corr-2", ["transport-order-intake"], Now.AddHours(-2), Now.AddHours(-2), null, []));

        var page = RenderAt("/?key=TO-2026-000123");

        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("corr-1").And.Contain("transport-order-intake")
            .And.Contain("Sat 26 Sep 2026, 11:55:00").And.Contain("Sat 26 Sep 2026, 11:56:00")
            .And.Contain("Succeeded").And.Contain("Transport Order TO-2026-000123").And.Contain("Source message evt-5c1d");
        rows[1].TextContent.Should().Contain("corr-2").And.Contain("No outcome recorded");
    }

    [Fact]
    public void Render_WithTrailsFound_ShowsKeysOnly_NeverABody()
    {
        // Bodies hold personal data; they show only when an operator opens a step (docs/operations/integration-console.md).
        Answer(new TrailSummary("corr-1", ["transport-order-intake"], Now, Now, Outcome.Succeeded, []));

        var page = RenderAt("/?key=corr-1");

        page.FindAll("thead th").Select(header => header.TextContent.Trim())
            .Should().Equal("Correlation ID", "Flows", "First seen", "Last seen", "Outcome", "Business keys");
    }

    [Fact]
    public void Render_WithTrailsFound_LinksEachToItsMessageTrail()
    {
        Answer(new TrailSummary("5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a", ["transport-order-intake"], Now, Now, Outcome.Succeeded, []));

        var page = RenderAt("/?key=TO-2026-000123");

        var link = page.Find("tbody tr a");
        link.TextContent.Trim().Should().Be("5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a");
        link.GetAttribute("href").Should().Be("/trails/5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a");
    }

    [Fact]
    public void Render_WithAFailedTrail_MarksIt()
    {
        Answer(new TrailSummary("corr-1", ["transport-order-intake"], Now, Now, Outcome.Failed, []));

        var page = RenderAt("/?key=corr-1");

        page.Find("tbody tr [data-outcome=Failed]").TextContent.Trim().Should().Be("Failed");
    }

    [Fact]
    public void Render_WithTrailsFound_SaysWhatWasSearched_AndThatTelemetryMayLag()
    {
        Answer(new TrailSummary("corr-1", ["transport-order-intake"], Now, Now, Outcome.Succeeded, []));

        var page = RenderAt("/?key=corr-1");

        page.Find("[role=status]").TextContent.Should()
            .Contain("1 Message trail carried corr-1 between Fri 25 Sep 2026, 12:00:00 and Sat 26 Sep 2026, 12:00:00.")
            .And.Contain("Telemetry may be a few minutes behind.");
    }

    [Fact]
    public void Render_WhileSearching_SaysSo()
    {
        _trails.SearchAsync(Arg.Any<SearchTrailsQuery>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<Result<TrailSearchResult>>().Task);

        var page = RenderAt("/?key=TO-2026-000123");

        page.Find("[role=status]").TextContent.Should().Contain("Searching for TO-2026-000123");
    }

    [Fact]
    public void Render_WhenNothingIsFound_SaysSo_AndThatTelemetryMayLag()
    {
        var page = RenderAt("/?key=TO-2026-000999");

        page.Find("[role=status]").TextContent.Should()
            .Contain("No Message trail carried TO-2026-000999 between Fri 25 Sep 2026, 12:00:00 and Sat 26 Sep 2026, 12:00:00.")
            .And.Contain("Telemetry may be a few minutes behind.");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenTheSearchFails_SaysSo()
    {
        _trails.SearchAsync(Arg.Any<SearchTrailsQuery>(), Arg.Any<CancellationToken>())
            .Returns<Result<TrailSearchResult>>(_ => throw new InvalidOperationException("Log Analytics is down."));

        var page = RenderAt("/?key=TO-2026-000123");

        page.Find("[role=alert]").TextContent.Should().Contain("The search couldn't be run.");
        page.Markup.Should().NotContain("Log Analytics is down.");
    }

    [Fact]
    public void Render_WhenTheSearchIsInvalid_SaysWhy()
    {
        _trails.SearchAsync(Arg.Any<SearchTrailsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<TrailSearchResult>.Invalid(new ValidationError("Key", "Type a correlation ID or a business key to search for.")));

        var page = RenderAt("/?key=%20");

        page.Find("[role=alert]").TextContent.Should().Contain("Type a correlation ID or a business key to search for.");
    }

    private IRenderedComponent<Search> RenderAt(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return Render<Search>();
    }

    private void Answer(params TrailSummary[] trails) =>
        _trails.SearchAsync(Arg.Any<SearchTrailsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TrailSearchResult(Now.AddHours(-24), Now, trails)));
}

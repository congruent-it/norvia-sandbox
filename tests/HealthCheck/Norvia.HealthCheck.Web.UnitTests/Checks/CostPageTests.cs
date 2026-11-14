// NSubstitute sets up a task-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using Ardalis.Result;
using Norvia.Agents;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Narrate;
using Norvia.HealthCheck.Application.Operators;
using Norvia.HealthCheck.Web.Checks;
using Norvia.HealthCheck.Web.Operators;

namespace Norvia.HealthCheck.Web.UnitTests.Checks;

public sealed class CostPageTests : HealthCheckWebContext
{
    private static CostView Rising(bool provisional = false) => new(
        7,
        [
            new(new DateOnly(2026, 11, 11), 40, false, [new("log-workspace", 3m), new("sql", 1m)]),
            new(new DateOnly(2026, 11, 12), 40, provisional, [new("log-workspace", 3m), new("sql", 1m)]),
        ],
        [new("log-workspace", 1m, 3m), new("sql", 1m, 1m)],
        new("Total", 2m, 4m, ChangeKind.Total),
        new("Transport Orders", 40m, 40m, ChangeKind.Volume));

    public CostPageTests() => Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Cost).SetPolicies(OperatorSignIn.CostPolicy);

    private IRenderedComponent<CostPage> Render() => Render<CostPage>();

    [Fact]
    public void Render_ShowsEachResourcesChangeNextToVolumesChange()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=change][data-resource=log-workspace]").TextContent.Should().Be("+200%"));
        page.Find("[data-testid=change][data-resource=sql]").TextContent.Should().Be("0%");
        page.Find("[data-testid=change][data-kind=Volume]").TextContent.Should().Be("0%");
        page.Find("[data-testid=change][data-kind=Total]").TextContent.Should().Be("+100%");
    }

    [Fact]
    public void Render_ShowsSpendPerResourcePerDayWithTheDaysVolume()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());

        var page = Render();

        var days = () => page.Find("section[aria-label='Spend per day']");
        page.WaitForAssertion(() => days().QuerySelectorAll("[role=row]").Should().HaveCount(3));
        days().QuerySelectorAll("[role=row]")[0].Children.Select(header => header.TextContent.Trim()).Should().Equal("Day", "log-workspace", "sql", "Total", "Transport Orders");
        days().QuerySelectorAll("[role=row]")[1].TextContent.Should().Contain("2026-11-11").And.Contain("3.00").And.Contain("4.00").And.Contain("40");
    }

    [Fact]
    public void Render_WithAProvisionalDay_ShowsTheLagAndMarksTheDay()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising(provisional: true));

        var page = Render();

        page.WaitForAssertion(() => page.Find("[role=note]").TextContent.Should().Contain("provisional"));
        page.Find("section[aria-label='Spend per day']").QuerySelectorAll("[role=row]")[2].TextContent.Should().Contain("2026-11-12").And.Contain("(provisional)");
    }

    [Fact]
    public void Render_WithoutProvisionalDays_StillSaysCostLagsAWholeDay()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());

        var page = Render();

        page.WaitForAssertion(() => page.Find("[role=note]").TextContent.Should().Contain("about a day late").And.NotContain("provisional"));
    }

    [Fact]
    public void Render_NoPreviousPeriod_SaysNoDataAndNoChange()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising() with { Total = new("Total", null, 4m, ChangeKind.Total) });

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=change][data-kind=Total]").TextContent.Should().Be("n/a"));
        page.Markup.Should().Contain("no data");
    }

    [Fact]
    public void Render_WhileReading_ShowsItIsWaiting()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<Result<CostView>>().Task);

        Render().Find("[role=status]").TextContent.Should().Contain("Reading the estate's spend");
    }

    [Fact]
    public void Render_WithNoDays_SaysThereIsNoSpendYet()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(new CostView(7, [], [], new("Total", null, null, ChangeKind.Total), new("Transport Orders", null, null, ChangeKind.Volume)));

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=no-spend]").TextContent.Should().Contain("No spend to show yet"));
    }

    [Fact]
    public void Render_WhenTheReaderFails_ShowsAnErrorAndNoNumbers()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns<Result<CostView>>(_ => throw new InvalidOperationException("Cost Management is not in EUR"));

        var page = Render();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("could not be read"));
        page.Markup.Should().NotContain("Cost Management is not in EUR").And.NotContain("Spend per day");
    }

    [Fact]
    public void Render_AResourceNameWithMarkup_IsShownAsText()
    {
        var view = Rising() with { Resources = [new("<b>evil</b>", 1m, 2m)] };
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(view);

        var page = Render();

        page.WaitForAssertion(() => page.Find("section[aria-label='Change against the previous period']").TextContent.Should().Contain("<b>evil</b>"));
        page.FindAll("section[aria-label='Change against the previous period'] b").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithTheAgentsOff_HasNoExplainButton()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=change][data-kind=Total]"));
        page.FindAll("[data-testid=explain-spend]").Should().BeEmpty();
    }

    [Fact]
    public void Explain_ShowsWhatTheFinOpsAgentSaid_AsText()
    {
        TurnAgentsOn();
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());
        Requests.NarrateSpendAsync(Arg.Any<NarrateSpendQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Narration("Total daily spend changed +100%.", [new NarratedItem("F-COST-RISING-SPEND", "<b>log-workspace</b> drives it; the toggle is chattyTelemetry.", "r2", "get_rising_spend_findings")]));
        var page = Render();
        page.WaitForAssertion(() => page.Find("[data-testid=explain-spend]"));

        page.Find("[data-testid=explain-spend]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=spend-narration-summary]").TextContent.Should().Be("Total daily spend changed +100%."));
        var item = page.Find("[data-testid=spend-narration-item]");
        item.TextContent.Should().Contain("F-COST-RISING-SPEND").And.Contain("<b>log-workspace</b> drives it").And.Contain("[r2, get_rising_spend_findings]");
        item.QuerySelector("b").Should().BeNull("what the agent wrote is data, never markup");
        Requests.Received(1).NarrateSpendAsync(Arg.Is<NarrateSpendQuery>(query => query.CorrelationId.Length == 32), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Explain_WhenTheAgentIsUnavailable_SaysSo_AndTheSpendStaysOnThePage()
    {
        TurnAgentsOn();
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());
        Requests.NarrateSpendAsync(Arg.Any<NarrateSpendQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Narration>.Unavailable("AgentUnavailable"));
        var page = Render();
        page.WaitForAssertion(() => page.Find("[data-testid=explain-spend]"));

        page.Find("[data-testid=explain-spend]").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
        page.Find("[data-testid=change][data-kind=Total]").TextContent.Should().Be("+100%");
    }
}

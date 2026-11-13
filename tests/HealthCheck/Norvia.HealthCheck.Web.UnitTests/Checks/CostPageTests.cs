// NSubstitute sets up a task-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using Ardalis.Result;
using Norvia.HealthCheck.Application.Cost;
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
        new("Total", 2m, 4m),
        new("Transport Orders", 40m, 40m));

    public CostPageTests() => Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Cost).SetPolicies(OperatorSignIn.CostPolicy);

    private IRenderedComponent<CostPage> Render() => Render<CostPage>();

    [Fact]
    public void Render_ShowsEachResourcesChangeNextToVolumesChange()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=change][data-resource=log-workspace]").TextContent.Should().Be("+200%"));
        page.Find("[data-testid=change][data-resource=sql]").TextContent.Should().Be("0%");
        page.Find("[data-testid=change][data-resource='Transport Orders']").TextContent.Should().Be("0%");
        page.Find("[data-testid=change][data-resource=Total]").TextContent.Should().Be("+100%");
    }

    [Fact]
    public void Render_ShowsSpendPerResourcePerDayWithTheDaysVolume()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());

        var page = Render();

        page.WaitForAssertion(() => page.FindAll("[data-testid=day]").Should().HaveCount(2));
        page.FindAll("[data-testid=days] thead th").Select(header => header.TextContent).Should().Equal("Day", "log-workspace", "sql", "Total", "Transport Orders");
        page.FindAll("[data-testid=day]")[0].TextContent.Should().Contain("2026-11-11").And.Contain("3.00").And.Contain("4.00").And.Contain("40");
    }

    [Fact]
    public void Render_WithAProvisionalDay_ShowsTheLagAndMarksTheDay()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising(provisional: true));

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=lag-note]").TextContent.Should().Contain("provisional"));
        page.FindAll("[data-testid=day]")[1].GetAttribute("data-provisional").Should().Be("true");
        page.FindAll("[data-testid=day]")[1].TextContent.Should().Contain("(provisional)");
    }

    [Fact]
    public void Render_WithoutProvisionalDays_StillSaysCostLagsAWholeDay()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising());

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=lag-note]").TextContent.Should().Contain("about a day late").And.NotContain("provisional"));
    }

    [Fact]
    public void Render_NoPreviousPeriod_SaysNoDataAndNoChange()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(Rising() with { Total = new("Total", null, 4m) });

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=change][data-resource=Total]").TextContent.Should().Be("n/a"));
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
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(new CostView(7, [], [], new("Total", null, null), new("Transport Orders", null, null)));

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=no-spend]").TextContent.Should().Contain("No spend to show yet"));
    }

    [Fact]
    public void Render_WhenTheReaderFails_ShowsAnErrorAndNoNumbers()
    {
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns<Result<CostView>>(_ => throw new InvalidOperationException("Cost Management is not in EUR"));

        var page = Render();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("could not be read"));
        page.Markup.Should().NotContain("Cost Management is not in EUR").And.NotContain("data-testid=\"day\"");
    }

    [Fact]
    public void Render_AResourceNameWithMarkup_IsShownAsText()
    {
        var view = Rising() with { Resources = [new("<b>evil</b>", 1m, 2m)] };
        Requests.CostAsync(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(view);

        var page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=changes]").TextContent.Should().Contain("<b>evil</b>"));
        page.FindAll("[data-testid=changes] b").Should().BeEmpty();
    }
}

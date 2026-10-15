using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Configure;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Web.Configure;
using Norvia.Simulator.Web.Operators;

namespace Norvia.Simulator.Web.UnitTests.Configure;

public sealed class ConfigureRunTests : SimulatorWebContext
{
    public ConfigureRunTests()
    {
        Requests.GetConfigureAsync(Arg.Any<CancellationToken>()).Returns(new ConfigureView(Presets.All, TestData.Limits, 777, 5));
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<ConfigureRun> page, string label)
    {
        var id = page.FindAll("label").Single(candidate => candidate.TextContent.Trim() == label).GetAttribute("for");
        return page.Find($"#{id}");
    }

    private string Address => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void Authorize_ConfiguringARun_RequiresTheRunRole()
    {
        typeof(ConfigureRun).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.RunPolicy);
    }

    [Fact]
    public void Render_OffersThePresetsAndStartsFromTheFirst()
    {
        var page = Render<ConfigureRun>();

        page.Find("h1").TextContent.Should().Be("Configure a run");
        page.Find("fluent-select").QuerySelectorAll("fluent-option").Select(option => option.TextContent.Trim())
            .Should().Equal("Custom", "Quiet Monday", "Busy day", "Peak week");
        Field(page, "Orders").GetAttribute("value").Should().Be("20");
        Field(page, "Seed").GetAttribute("value").Should().Be("777");
    }

    [Fact]
    public void Render_TellsTheCapsAndTheNextRunNumber()
    {
        var page = Render<ConfigureRun>();

        page.Markup.Should().Contain("At most 50 orders per run and 3 requests at once").And.Contain("SIM-5-1");
    }

    [Fact]
    public void ChoosingAPreset_FillsInItsNumbers()
    {
        var page = Render<ConfigureRun>();

        page.Find("fluent-select").Change("Busy day");

        Field(page, "Orders").GetAttribute("value").Should().Be("100");
        Field(page, "Bad bookings (%)").GetAttribute("value").Should().Be("10");
        Field(page, "Window (minutes)").GetAttribute("value").Should().Be("10");
    }

    [Fact]
    public void ChoosingABurst_HasNoWindow()
    {
        var page = Render<ConfigureRun>();

        page.FindAll("label").Should().NotContain(label => label.TextContent.Trim() == "Window (minutes)");
    }

    [Fact]
    public void ChoosingCustom_KeepsWhatWasThere()
    {
        var page = Render<ConfigureRun>();

        page.Find("fluent-select").Change("Custom");

        Field(page, "Orders").GetAttribute("value").Should().Be("20");
    }

    [Fact]
    public void NewSeed_OffersTheSeedTheApplicationGives()
    {
        Requests.GetConfigureAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ConfigureView(Presets.All, TestData.Limits, 777, 5)),
            Task.FromResult(new ConfigureView(Presets.All, TestData.Limits, 888, 5)));
        var page = Render<ConfigureRun>();

        page.Find("fluent-button:contains('New seed')").Click();

        Field(page, "Seed").GetAttribute("value").Should().Be("888");
    }

    [Fact]
    public void Render_TheDatabaseUnreachable_SaysSoInsteadOfLoadingForever()
    {
        Requests.GetConfigureAsync(Arg.Any<CancellationToken>()).Returns<ConfigureView>(_ => throw new InvalidOperationException("down"));

        var page = Render<ConfigureRun>();

        page.Find("[role=alert]").TextContent.Should().Contain("couldn't load");
    }

    [Fact]
    public void Render_HasAFieldForEachShareOfTheCarrierStage()
    {
        var page = Render<ConfigureRun>();

        foreach (var label in new[]
        {
            "Released by the Planner (%)", "Confirmed by the Carrier (%)", "Declined by the Carrier (%)", "Tracked (%)", "Exceptions on the way (%)",
            "Delivered (%)", "With a Proof of delivery (%)", "Failed collections cancelled (%)",
        })
        {
            Field(page, label).Should().NotBeNull(label);
        }
    }

    [Fact]
    public void Preview_GoesToThePlanPreviewWithTheSettingsInTheAddress()
    {
        var page = Render<ConfigureRun>();
        Field(page, "Orders").Change("30");

        page.Find("form").Submit();

        Address.Should().EndWith("/plan?orders=30&bad=5&today=30&seed=777&mode=Burst&window=0&assigned=90&ownfleet=60&reassign=5&release=100&confirm=80&decline=10&tracked=90&exception=10&delivered=80&pod=90&cancel=50&preset=Quiet%20Monday");
    }

    [Fact]
    public void Preview_AnOrderCountOverTheCap_IsRefusedHereWithoutLeaving()
    {
        var page = Render<ConfigureRun>();
        Field(page, "Orders").Change("51");

        page.Find("form").Submit();

        page.Find("[role=alert]").TextContent.Should().Contain("at most 50");
        Address.Should().NotContain("/plan");
    }

    [Fact]
    public void Preview_ACustomRun_HasNoPresetInTheAddress()
    {
        var page = Render<ConfigureRun>();
        page.Find("fluent-select").Change("Custom");

        page.Find("form").Submit();

        Address.Should().NotContain("preset=");
    }

    [Fact]
    public void Render_AnActiveRun_SaysSoAndLinksToIt()
    {
        var active = TestData.Summary();
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([active]);

        var page = Render<ConfigureRun>();

        page.Find("[role=status]").TextContent.Should().Contain("Run 4 is still sending");
        page.Find("[role=status] a").GetAttribute("href").Should().Be($"/runs/{active.Id}");
        page.Find("fluent-button:contains('Clear history')").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Render_PreviousRuns_AreListedWithHowFarTheyGot()
    {
        var finished = TestData.Summary(SimulationRunStatus.Completed, number: 3, sent: 10);
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([finished]);

        var page = Render<ConfigureRun>();

        var grid = page.Find("table");
        grid.TextContent.Should().Contain("Run 3").And.Contain("Completed").And.Contain("10 of 10").And.Contain("Quiet Monday");
        grid.QuerySelector("a")!.GetAttribute("href").Should().Be($"/runs/{finished.Id}");
    }

    [Fact]
    public void Render_NoRuns_SaysNoneYet()
    {
        Render<ConfigureRun>().Markup.Should().Contain("No runs yet.");
    }

    [Fact]
    public void ClearHistory_AsksTheApplicationAndReloadsTheRuns()
    {
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<RunSummary>>([TestData.Summary(SimulationRunStatus.Completed)]),
            Task.FromResult<IReadOnlyList<RunSummary>>([]));
        Requests.ClearHistoryAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
        var page = Render<ConfigureRun>();

        page.Find("fluent-button:contains('Clear history')").Click();

        Requests.Received(1).ClearHistoryAsync(Arg.Any<CancellationToken>());
        page.Markup.Should().Contain("No runs yet.");
    }

    [Fact]
    public void ClearHistory_Refused_ShowsWhy()
    {
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([TestData.Summary(SimulationRunStatus.Completed)]);
        Requests.ClearHistoryAsync(Arg.Any<CancellationToken>()).Returns(Result.Conflict("A run is active."));
        var page = Render<ConfigureRun>();

        page.Find("fluent-button:contains('Clear history')").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("A run is active.");
    }
}

using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Configure;
using Norvia.Simulator.Application.Reset;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Web.Configure;
using Norvia.Simulator.Web.Operators;
using Norvia.Simulator.Web.Plans;

namespace Norvia.Simulator.Web.UnitTests.Configure;

public sealed class ConfigureRunTests : SimulatorWebContext
{
    public ConfigureRunTests()
    {
        Requests.GetConfigureAsync(Arg.Any<CancellationToken>()).Returns(new ConfigureView(Presets.All, TestData.Limits, 777, 5));
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Requests.GetResetReadinessAsync(Arg.Any<CancellationToken>()).Returns(Ready);
    }

    private static EstateResetReadiness Ready { get; } =
        new(false, EstateApps.All.Select(app => new AppReadiness(app, Reachable: true, Enabled: true, BusySubscription: null)).ToList());

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
    public void Render_WithTheSettingsOfARunInTheAddress_FillsTheFormWithThem()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(PlanLinks.Configure(TestData.Settings(seed: 55, preset: "Busy day", mode: RunMode.Paced)));

        var page = Render<ConfigureRun>();

        Field(page, "Orders").GetAttribute("value").Should().Be("10");
        Field(page, "Collected today (%)").GetAttribute("value").Should().Be("30");
        Field(page, "Assigned by the Planner (%)").GetAttribute("value").Should().Be("70");
        Field(page, "Confirmed by the Carrier (%)").GetAttribute("value").Should().Be("85");
        Field(page, "Own fleet put on Trips (%)").GetAttribute("value").Should().Be("90");
        Field(page, "Delivered with reservations (%)").GetAttribute("value").Should().Be("12");
        Field(page, "Window (minutes)").GetAttribute("value").Should().Be("15");
        Field(page, "Seed").GetAttribute("value").Should().Be("55");
        page.Find("fluent-select").GetAttribute("current-value").Should().Be("Busy day");
    }

    [Fact]
    public void Render_WithTheSettingsOfARunButNoSeed_OffersANewSeed()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(PlanLinks.ConfigureWithNewSeed(TestData.Settings(seed: 55)));

        var page = Render<ConfigureRun>();

        Field(page, "Orders").GetAttribute("value").Should().Be("10");
        Field(page, "Seed").GetAttribute("value").Should().Be("777");
    }

    [Fact]
    public void Render_WithASettingsPresetThatNoLongerExists_ShowsTheRunAsCustom()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(PlanLinks.Configure(TestData.Settings(preset: "Gone")));

        var page = Render<ConfigureRun>();

        page.Find("fluent-select").GetAttribute("current-value").Should().Be("Custom");
        Field(page, "Orders").GetAttribute("value").Should().Be("10");
    }

    [Fact]
    public void Navigate_ToTheSettingsOfARunWhileConfigureIsOpen_RefillsTheForm()
    {
        var page = Render<ConfigureRun>();

        Services.GetRequiredService<NavigationManager>().NavigateTo(PlanLinks.Configure(TestData.Settings(seed: 55)));

        page.WaitForAssertion(() => Field(page, "Orders").GetAttribute("value").Should().Be("10"));
        Field(page, "Seed").GetAttribute("value").Should().Be("55");
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

        Address.Should().EndWith("/plan?orders=30&bad=5&today=30&seed=777&mode=Burst&window=0&assigned=90&ownfleet=60&reassign=5&trips=100&release=100&confirm=80&decline=10&tracked=90&exception=10&delivered=80&pod=90&cancel=50&collfail=10&delfail=10&damaged=5&reservations=10&invoiced=100&credited=50&editedinvoices=50&rejectedlines=25&preset=Quiet%20Monday");
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
    public void Render_AnActiveRunThatFinishesMeanwhile_StopsSayingItIsSending()
    {
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([TestData.Summary()]);
        var page = Render<ConfigureRun>();
        page.Find("[role=status]").TextContent.Should().Contain("still sending");
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([TestData.Summary(SimulationRunStatus.Completed)]);

        page.InvokeAsync(() => Clock.Advance(TimeSpan.FromSeconds(1.5)));

        page.WaitForAssertion(() => page.Markup.Should().NotContain("still sending"));
        page.Find("fluent-button:contains('Clear history')").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Render_WithoutAnActiveRun_DoesNotPoll()
    {
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([TestData.Summary(SimulationRunStatus.Completed)]);
        Render<ConfigureRun>();

        Clock.Advance(TimeSpan.FromSeconds(30));

        _ = Requests.Received(1).ListRunsAsync(Arg.Any<CancellationToken>());
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
    public void Render_PreviousRuns_ShowWhenTheyEnded_AndADashForOneStillSending()
    {
        var finished = TestData.Summary(SimulationRunStatus.Completed, number: 3);
        var running = TestData.Summary(SimulationRunStatus.Running, number: 4);
        Requests.ListRunsAsync(Arg.Any<CancellationToken>()).Returns([running, finished]);

        var page = Render<ConfigureRun>();

        var rows = page.FindAll("tbody tr");
        rows[1].TextContent.Should().Contain(SimulatorText.DateAndTime(finished.StartedAt)).And.Contain(SimulatorText.DateAndTime(finished.FinishedAt!.Value));
        rows[0].Children[3].TextContent.Trim().Should().Be("–");
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

    [Fact]
    public void ResetEstate_WhenTheEstateIsQuiet_IsOffered()
    {
        var page = Render<ConfigureRun>();

        page.Find("fluent-button:contains('Reset estate')").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ResetEstate_WhenItCannotBeTaken_IsDisabledWithTheReason()
    {
        Requests.GetResetReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new EstateResetReadiness(false, [new AppReadiness(EstateApp.Tms, Reachable: true, Enabled: false, BusySubscription: null)]));
        var page = Render<ConfigureRun>();

        page.Find("fluent-button:contains('Reset estate')").HasAttribute("disabled").Should().BeTrue();
        page.Markup.Should().Contain("The reset is switched off for the TMS.");
    }

    [Fact]
    public void ResetEstate_ReadinessThatCannotBeRead_IsDisabledWithAReason()
    {
        Requests.GetResetReadinessAsync(Arg.Any<CancellationToken>()).Returns<Task<EstateResetReadiness>>(_ => throw new HttpRequestException());
        var page = Render<ConfigureRun>();

        page.Find("fluent-button:contains('Reset estate')").HasAttribute("disabled").Should().BeTrue();
        page.Markup.Should().Contain("couldn't check whether the estate would take a reset");
    }

    [Fact]
    public void ResetEstate_AsksForConfirmationFirst_SayingWhatGoesAndWhatStays()
    {
        var page = Render<ConfigureRun>();

        page.Find("fluent-button:contains('Reset estate')").Click();

        var dialog = page.Find("[role=alertdialog]");
        dialog.TextContent.Should().Contain("every Transport Order").And.Contain("SIM-1-1").And.Contain("Shippers, Carriers, Drivers, Vehicles, Rate Cards and Personas stay");
        Requests.DidNotReceive().ResetEstateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ResetEstate_KeepEverything_ResetsNothing()
    {
        var page = Render<ConfigureRun>();
        page.Find("fluent-button:contains('Reset estate')").Click();

        page.Find("fluent-button:contains('Keep everything')").Click();

        page.FindAll("[role=alertdialog]").Should().BeEmpty();
        Requests.DidNotReceive().ResetEstateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ResetEstate_Confirmed_ReportsWhatEachApplicationDeleted()
    {
        Requests.ResetEstateAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(new EstateResetReport(
            [
                AppResetResult.Done(EstateApp.Tms, [new DeletedKind("Transport Orders", 12)]),
                AppResetResult.Done(EstateApp.PartnerPortal, []),
                AppResetResult.Done(EstateApp.DriverApp, [new DeletedKind("Trips", 2), new DeletedKind("Driver reports", 9)]),
            ],
            [new DeletedKind("Runs", 1)])));
        var page = Render<ConfigureRun>();
        page.Find("fluent-button:contains('Reset estate')").Click();

        page.Find("fluent-button:contains('Delete everything and reset')").Click();

        var result = page.Find("[aria-label='Reset result']").TextContent;
        result.Should().Contain("The estate is a clean sheet.").And.Contain("TMS: deleted 12 Transport Orders.")
            .And.Contain("Partner portal: nothing to delete.").And.Contain("Driver app: deleted 2 Trips, 9 Driver reports.").And.Contain("Simulator: deleted 1 Runs.");
    }

    [Fact]
    public void ResetEstate_AnApplicationRefused_SaysWhichAndThatItCanBeRunAgain()
    {
        Requests.ResetEstateAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(new EstateResetReport(
            [
                AppResetResult.Done(EstateApp.Tms, [new DeletedKind("Transport Orders", 12)]),
                AppResetResult.Refused(EstateApp.PartnerPortal, "It answered 500."),
                AppResetResult.Done(EstateApp.DriverApp, []),
            ],
            [])));
        var page = Render<ConfigureRun>();
        page.Find("fluent-button:contains('Reset estate')").Click();

        page.Find("fluent-button:contains('Delete everything and reset')").Click();

        var result = page.Find("[aria-label='Reset result']").TextContent;
        result.Should().Contain("Not everything was reset").And.Contain("can be run again").And.Contain("Partner portal: refused. It answered 500.");
        result.Should().NotContain("Simulator:");
    }

    [Fact]
    public void ResetEstate_NotTried_ShowsWhy()
    {
        Requests.ResetEstateAsync(Arg.Any<CancellationToken>()).Returns(Result<EstateResetReport>.Conflict("The subscription trip-events/trip-dispatch holds unprocessed messages."));
        var page = Render<ConfigureRun>();
        page.Find("fluent-button:contains('Reset estate')").Click();

        page.Find("fluent-button:contains('Delete everything and reset')").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("trip-events/trip-dispatch");
    }

    [Fact]
    public void ResetEstate_TheSimulatorFails_SaysWhatWasKept()
    {
        Requests.ResetEstateAsync(Arg.Any<CancellationToken>()).Returns<Task<Result<EstateResetReport>>>(_ => throw new InvalidOperationException());
        var page = Render<ConfigureRun>();
        page.Find("fluent-button:contains('Reset estate')").Click();

        page.Find("fluent-button:contains('Delete everything and reset')").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("What was deleted stays deleted");
    }

    private void GivenHostileChoices() =>
        Requests.GetConfigureAsync(Arg.Any<CancellationToken>()).Returns(new ConfigureView(Presets.All, TestData.Limits, 777, 5)
        {
            HostileCarriers = [new HostileChoice("CAR-0001", "Van Dijk Transport"), new HostileChoice("CAR-0002", "Kessler Spedition")],
            HostileDrivers = [new HostileChoice("DRV-0001", "Anouk de Vries")],
        });

    [Fact]
    public void Render_OffersTheHostileProfileOneCarrierAndDriverAtATime_AllOffToStartWith()
    {
        GivenHostileChoices();

        var page = Render<ConfigureRun>();

        page.Markup.Should().Contain("Hostile profile (off by default)").And.Contain("profile: hostile");
        var choices = page.Find("[data-testid=hostile-choices]").QuerySelectorAll("fluent-checkbox");
        choices.Select(choice => choice.TextContent.Trim()).Should().Equal("Van Dijk Transport (CAR-0001)", "Kessler Spedition (CAR-0002)", "Anouk de Vries (DRV-0001)");
        choices.Should().OnlyContain(choice => !choice.ClassList.Contains("checked"), "nothing is hostile unless the operator ticks it");
    }

    [Fact]
    public void Render_WithTheSettingsOfAHostileRun_TicksItsCarriersAndDrivers()
    {
        GivenHostileChoices();
        Services.GetRequiredService<NavigationManager>().NavigateTo(PlanLinks.Configure(TestData.Settings(seed: 55) with { HostileCarrierIds = ["CAR-0002"], HostileDriverIds = ["DRV-0001"] }));

        var page = Render<ConfigureRun>();

        var ticked = page.Find("[data-testid=hostile-choices]").QuerySelectorAll("fluent-checkbox").Where(choice => choice.ClassList.Contains("checked")).Select(choice => choice.TextContent.Trim()).ToList();
        ticked.Should().Equal("Kessler Spedition (CAR-0002)", "Anouk de Vries (DRV-0001)");
    }
}

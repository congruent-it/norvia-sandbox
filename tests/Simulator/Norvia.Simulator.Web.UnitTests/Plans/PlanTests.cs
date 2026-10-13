using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Web.Operators;
using Norvia.Simulator.Web.Plans;

namespace Norvia.Simulator.Web.UnitTests.Plans;

public sealed class PlanTests : SimulatorWebContext
{
    private const string Address = "/plan?orders=3&bad=34&today=30&seed=77&mode=Paced&window=10&preset=Busy%20day";

    private static readonly SimulationSettings Settings = new(3, 34, 30, 77, RunMode.Paced, TimeSpan.FromMinutes(10), "Busy day");

    private static PlanPreview Preview() => new(
        5, Settings,
        [new ShipperShare("SHP-0001", "Brouwer Dranken", 2, 1), new ShipperShare("SHP-0003", "Tuincentrum Groenrijk", 1, 0)],
        [
            new PlanRow(1, "Brouwer Dranken", "Brouwer Dranken", "SIM-5-1", "Rotterdam → Antwerpen", 0, 420m, null, ExpectedAnswer.Accepted),
            new PlanRow(2, "Brouwer Dranken", "Brouwer Dranken", "SIM-5-2", "Rotterdam → Germany", 3, 120m, BadBookingKind.NoLane, ExpectedAnswer.Accepted),
            new PlanRow(3, "Tuincentrum Groenrijk", "Tuincentrum Groenrijk", "SIM-5-3", "Utrecht → Utrecht", 1, 60m, null, ExpectedAnswer.Accepted),
        ]);

    private IRenderedComponent<Plan> RenderAt(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return Render<Plan>();
    }

    private string Current => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void Authorize_PreviewingAPlan_RequiresTheRunRole()
    {
        typeof(Plan).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.RunPolicy);
    }

    [Fact]
    public void Render_WithoutSettingsInTheAddress_LeadsBackToConfigure()
    {
        var page = RenderAt("/plan");

        page.Find("[role=alert]").TextContent.Should().Contain("no settings");
        page.Find("[role=alert] a").GetAttribute("href").Should().Be("/");
        Requests.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Render_WithSettings_PreviewsThePlanTheyGive()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());

        var page = RenderAt(Address);

        Requests.Received(1).PreviewAsync(Settings, Arg.Any<CancellationToken>());
        page.Find("[role=status]").TextContent.Should().Contain("Run 5 would send 3 bookings, 1 of them bad").And.Contain("seed 77").And.Contain("paced over 10 min");
    }

    [Fact]
    public void Render_ThePlan_ShowsPerShipperAndPerBooking()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());

        var page = RenderAt(Address);

        var grids = page.FindAll("table");
        grids[0].TextContent.Should().Contain("Brouwer Dranken").And.Contain("Tuincentrum Groenrijk");
        grids[1].TextContent.Should().Contain("SIM-5-2").And.Contain("Rotterdam → Germany").And.Contain("No Lane").And.Contain("in 3 days").And.Contain("today").And.Contain("Good");
    }

    [Fact]
    public void Render_ASettingsSetOverTheCap_ShowsWhyAndNoPlan()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<PlanPreview>.Invalid(new ValidationError("A run may hold at most 50 orders; this one has 51.")));

        var page = RenderAt(Address);

        page.Find("[role=alert]").TextContent.Should().Contain("at most 50");
        page.FindAll("fluent-button").Should().NotContain(button => button.TextContent.Contains("Start run", StringComparison.Ordinal));
    }

    [Fact]
    public void StartRun_StartsTheSamePlanAsTheOperator_AndOpensTheRun()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());
        var started = TestData.Summary();
        Requests.StartAsync(Arg.Any<SimulationSettings>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(started);
        var page = RenderAt(Address);

        page.Find("fluent-button:contains('Start run')").Click();

        Requests.Received(1).StartAsync(Settings, "Dev Eloper", Arg.Any<CancellationToken>());
        Current.Should().EndWith($"/runs/{started.Id}");
    }

    [Fact]
    public void StartRun_WhileAnotherRunIsActive_ShowsWhyAndStaysOnThePlan()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());
        Requests.StartAsync(Arg.Any<SimulationSettings>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Norvia.Simulator.Application.Runs.RunSummary>.Conflict("A run is already active."));
        var page = RenderAt(Address);

        page.Find("fluent-button:contains('Start run')").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("A run is already active.");
        Current.Should().Contain("/plan");
    }

    [Fact]
    public void BackToConfigure_GoesBack()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());
        var page = RenderAt(Address);

        page.Find("fluent-button:contains('Back to Configure')").Click();

        Current.Should().EndWith("/");
    }

    [Fact]
    public void PlanLinks_Settings_RoundTripThroughTheAddress()
    {
        var link = PlanLinks.Plan(Settings);

        link.Should().Be(Address);
        PlanLinks.Settings(3, 34, 30, 77, "Paced", 10, "Busy day").Should().Be(Settings);
    }

    [Theory]
    [InlineData(null, 1, 1, 1, "Burst")]
    [InlineData(1, null, 1, 1, "Burst")]
    [InlineData(1, 1, null, 1, "Burst")]
    [InlineData(1, 1, 1, null, "Burst")]
    [InlineData(1, 1, 1, 1, "Slow")]
    [InlineData(1, 1, 1, 1, null)]
    public void PlanLinks_AnAddressWithoutAFullSetOfSettings_GivesNone(int? orders, int? bad, int? today, int? seed, string? mode)
    {
        PlanLinks.Settings(orders, bad, today, seed, mode, window: null, preset: null).Should().BeNull();
    }

    [Fact]
    public void PlanLinks_ACustomRun_NamesNoPreset()
    {
        PlanLinks.Plan(Settings with { PresetName = null, Mode = RunMode.Burst, Window = TimeSpan.Zero }).Should().Be("/plan?orders=3&bad=34&today=30&seed=77&mode=Burst&window=0");
    }
}

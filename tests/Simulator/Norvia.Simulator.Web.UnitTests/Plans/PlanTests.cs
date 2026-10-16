using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Web.Operators;
using Norvia.Simulator.Web.Plans;

namespace Norvia.Simulator.Web.UnitTests.Plans;

public sealed class PlanTests : SimulatorWebContext
{
    private const string Address = "/plan?orders=3&bad=34&today=30&seed=77&mode=Paced&window=10&assigned=60&ownfleet=40&reassign=20&trips=100&release=90&confirm=70&decline=15&tracked=90&exception=10&delivered=80&pod=85&cancel=50&collfail=10&delfail=10&damaged=5&reservations=10&preset=Busy%20day";

    private static readonly SimulationSettings Settings = new(3, 34, 30, 77, RunMode.Paced, TimeSpan.FromMinutes(10), "Busy day", 60, 40, 20);

    private static PlanPreview Preview() => new(
        5, Settings,
        [new ShipperShare("SHP-0001", "Brouwer Dranken", 2, 1), new ShipperShare("SHP-0003", "Tuincentrum Groenrijk", 1, 0)],
        [
            new PlanRow(1, "Brouwer Dranken", "Brouwer Dranken", "SIM-5-1", "Rotterdam → Antwerpen", 0, 420m, null, ExpectedAnswer.Accepted, PlannerIntent.OwnFleetIfItFits, Reassign: true),
            new PlanRow(2, "Brouwer Dranken", "Brouwer Dranken", "SIM-5-2", "Rotterdam → Germany", 3, 120m, BadBookingKind.NoLane, ExpectedAnswer.Accepted),
            new PlanRow(3, "Tuincentrum Groenrijk", "Tuincentrum Groenrijk", "SIM-5-3", "Utrecht → Utrecht", 1, 60m, null, ExpectedAnswer.Accepted, PlannerIntent.Carrier),
        ],
        new PlannerPreview(Orders: 3, Assigned: 2, TryOwnFleet: 1, Carrier: 1, Reassigned: 1),
        new CarrierPreview(new CarrierShares(), CarrierFunnel.Count(CarrierFunnel.Plan(new CarrierShares(), 77, 20))),
        new DriverPreview(new DriverShares(), DriverFunnel.Count(DriverFunnel.Plan(new DriverShares(), 77, 10))));

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
    public void Render_ThePlanCantBeMade_SaysSo()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>())
            .Returns<Result<PlanPreview>>(_ => throw new InvalidOperationException("down"));

        RenderAt(Address).Find("[role=alert]").TextContent.Should().Contain("couldn't be made");
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
        PlanLinks.Settings(3, 34, 30, 77, "Paced", 10, "Busy day", 60, 40, 20).Should().Be(Settings);
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
        PlanLinks.Plan(Settings with { PresetName = null, Mode = RunMode.Burst, Window = TimeSpan.Zero }).Should().Be("/plan?orders=3&bad=34&today=30&seed=77&mode=Burst&window=0&assigned=60&ownfleet=40&reassign=20&trips=100&release=90&confirm=70&decline=15&tracked=90&exception=10&delivered=80&pod=85&cancel=50&collfail=10&delfail=10&damaged=5&reservations=10");
    }

    [Fact]
    public void PlanLinks_AnAddressFromBeforeThePlanner_GetsTheDefaultShares()
    {
        var settings = PlanLinks.Settings(3, 34, 30, 77, "Burst", window: null, preset: null);

        (settings!.AssignedPercent, settings.OwnFleetPercent, settings.ReassignPercent)
            .Should().Be((SimulationSettings.DefaultAssignedPercent, SimulationSettings.DefaultOwnFleetPercent, SimulationSettings.DefaultReassignPercent));
    }

    [Fact]
    public void Render_ThePlan_ShowsTheAssignmentStageOfTheFunnel()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());

        var page = RenderAt(Address);

        page.Find("[data-testid=planner-funnel]").TextContent.Should()
            .Contain("assigns 2 of the 3 orders").And.Contain("(60%)").And.Contain("1 to Own fleet where it fits, 1 to a Carrier").And.Contain("reassigns 1 (20%)");
    }

    [Fact]
    public void Render_ThePlan_ShowsWhatThePlannerMeansToDoPerBooking()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());

        var bookings = RenderAt(Address).FindAll("table")[1].TextContent;

        bookings.Should().Contain("Own fleet if it fits, else a Carrier, then reassigned").And.Contain("A Carrier").And.Contain("Not assigned");
    }

    [Fact]
    public void Render_ThePlan_ShowsTheCarrierStageOfTheFunnel()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());

        var page = RenderAt(Address);

        page.Find("[data-testid=carrier-funnel]").TextContent.Should()
            .Contain("Of the 20 Shipments meant for a Carrier").And.Contain("releases 18 (90%)").And.Contain("confirm 13, decline 3 and leave 2 unanswered")
            .And.Contain("most a run can reach");
    }

    [Fact]
    public void Render_ThePlan_ShowsTheDriverStageOfTheFunnel()
    {
        Requests.PreviewAsync(Arg.Any<SimulationSettings>(), Arg.Any<CancellationToken>()).Returns(Preview());

        var page = RenderAt(Address);

        page.Find("[data-testid=driver-funnel]").TextContent.Should()
            .Contain("Of the 10 Shipments meant for Own fleet that collect today").And.Contain("Trips for later days stay Dispatched");
    }

    [Fact]
    public void PlanLinks_DriverShares_RoundTripThroughTheAddress()
    {
        var settings = Settings with { Drivers = new DriverShares(20, 25, 7, 40) };

        var link = PlanLinks.Plan(settings);

        link.Should().Contain("collfail=20&delfail=25&damaged=7&reservations=40");
        PlanLinks.Settings(3, 34, 30, 77, "Paced", 10, "Busy day", 60, 40, 20, collectionFailed: 20, deliveryFailed: 25, damaged: 7, reservations: 40).Should().Be(settings);
    }

    [Fact]
    public void PlanLinks_TheTripShare_RoundTripsThroughTheAddress_AndAnOlderAddressPutsAllOnTrips()
    {
        var settings = Settings with { TripPercent = 35 };

        PlanLinks.Plan(settings).Should().Contain("&trips=35&");
        PlanLinks.Settings(3, 34, 30, 77, "Paced", 10, "Busy day", 60, 40, 20, trips: 35).Should().Be(settings);
        PlanLinks.Settings(3, 34, 30, 77, "Burst", window: null, preset: null)!.TripPercent.Should().Be(100);
    }

    [Fact]
    public void PlanLinks_AnAddressFromBeforeTheDriverStage_GetsTheDefaultDriverShares()
    {
        PlanLinks.Settings(3, 34, 30, 77, "Burst", window: null, preset: null)!.Drivers.Should().Be(new DriverShares());
    }

    [Fact]
    public void PlanLinks_CarrierShares_RoundTripThroughTheAddress()
    {
        var settings = Settings with { Carriers = new CarrierShares(80, 60, 20, 85, 20, 70, 80, 40) };

        var link = PlanLinks.Plan(settings);

        link.Should().Contain("release=80&confirm=60&decline=20&tracked=85&exception=20&delivered=70&pod=80&cancel=40");
        PlanLinks.Settings(3, 34, 30, 77, "Paced", 10, "Busy day", 60, 40, 20, 80, 60, 20, 85, 20, 70, 80, 40).Should().Be(settings);
    }

    [Fact]
    public void PlanLinks_AnAddressFromBeforeTheCarrierStage_GetsTheDefaultCarrierShares()
    {
        PlanLinks.Settings(3, 34, 30, 77, "Burst", window: null, preset: null)!.Carriers.Should().Be(new CarrierShares());
    }
}

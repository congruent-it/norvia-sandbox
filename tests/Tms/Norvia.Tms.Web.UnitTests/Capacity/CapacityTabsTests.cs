using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Web.Capacity;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Capacity;

public sealed class CapacityTabsTests : TmsWebContext
{
    private static readonly VehicleView Van = new("VEH-0001", "VN-101-A", VehicleType.Van, 1200, 4, LicenceCategory.B);
    private static readonly VehicleView Truck = new("VEH-0003", "RT-201-C", VehicleType.RigidTruck, 10000, 18, LicenceCategory.C);
    private static readonly DriverView Anouk = new("DRV-0001", "Anouk de Vries", [LicenceCategory.B, LicenceCategory.C, LicenceCategory.CE],
        [VehicleType.Van, VehicleType.RigidTruck, VehicleType.TractorWithTrailer]);
    private static readonly DriverView Sanne = new("DRV-0006", "Sanne Jansen", [LicenceCategory.B], [VehicleType.Van]);

    [Fact]
    public void Render_VehiclesTab_BeforeTheVehiclesAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<VehicleView>>();
        Queries.ListVehiclesAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<VehiclesTab>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Vehicles");
    }

    [Fact]
    public void Render_VehiclesTab_ListsEachVehicleWithItsTypePayloadPalletPlacesAndLicenceNeeded()
    {
        Queries.ListVehiclesAsync(Arg.Any<CancellationToken>()).Returns([Van, Truck]);

        var page = Render<VehiclesTab>();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList())
            .Should().SatisfyRespectively(
                first => first.Should().Equal("VN-101-A", "Van", "1,200 kg", "4", "B"),
                second => second.Should().Equal("RT-201-C", "Rigid truck", "10,000 kg", "18", "C")));
    }

    [Fact]
    public void Render_VehiclesTab_WithNoVehicles_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListVehiclesAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<VehiclesTab>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("There are no Vehicles in the Own fleet."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_VehiclesTab_WhenAVehicleAppearsWhileTheTabIsOpen_ShowsItWithoutARefresh()
    {
        Queries.ListVehiclesAsync(Arg.Any<CancellationToken>()).Returns([Van], [Van, Truck]);
        var page = Render<VehiclesTab>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("VN-101-A"));

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Markup.Should().Contain("RT-201-C"));
    }

    [Fact]
    public void Render_DriversTab_ListsEachDriverWithTheirLicencesAndTheVehicleTypesTheyCanDrive()
    {
        Queries.ListDriversAsync(Arg.Any<CancellationToken>()).Returns([Anouk, Sanne]);

        var page = Render<DriversTab>();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList())
            .Should().SatisfyRespectively(
                first => first.Should().Equal("Anouk de Vries", "B, C, CE", "Van, Rigid truck, Tractor with trailer"),
                second => second.Should().Equal("Sanne Jansen", "B", "Van")));
    }

    [Fact]
    public void Render_DriversTab_WhenADriverAppearsWhileTheTabIsOpen_ShowsThemWithoutARefresh()
    {
        Queries.ListDriversAsync(Arg.Any<CancellationToken>()).Returns([Anouk], [Anouk, Sanne]);
        var page = Render<DriversTab>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Anouk de Vries"));

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Markup.Should().Contain("Sanne Jansen"));
    }

    [Fact]
    public void Render_DriversTab_WhenTheFirstReadFails_SaysSo()
    {
        Queries.ListDriversAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<DriverView>>(_ => throw new InvalidOperationException("down"));

        var page = Render<DriversTab>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The Drivers couldn't be loaded"));
    }

    [Fact]
    public void Render_EitherTab_LinksToBothTabs()
    {
        Queries.ListVehiclesAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<VehiclesTab>();

        page.FindAll("nav[aria-label=Capacity] a").Select(link => (link.GetAttribute("href"), link.TextContent.Trim()))
            .Should().Equal(("/capacity", "Vehicles"), ("/capacity/drivers", "Drivers"), ("/capacity/days", "Days"));
    }

    [Fact]
    public void Render_DaysTab_ListsOneRowPerOpenDay_WithVehiclesAvailableAndTheDriversOnShift()
    {
        Queries.ListPlanningDaysAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PlanningDayView(new DateOnly(2026, 10, 12), 6, [new ShiftView("DRV-0001", "Anouk de Vries", new TimeOnly(6, 0), new TimeOnly(14, 30), false)]),
            new PlanningDayView(new DateOnly(2026, 10, 17), 5, []),
            new PlanningDayView(new DateOnly(2026, 10, 13), 6,
            [
                new ShiftView("DRV-0001", "Anouk de Vries", new TimeOnly(5, 0), new TimeOnly(13, 0), true),
                new ShiftView("DRV-0002", "Joost Bakker", new TimeOnly(7, 0), new TimeOnly(15, 30), false),
            ]),
        ]);

        var page = Render<DaysTab>();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList())
            .Should().SatisfyRespectively(
                first => first.Should().Equal("Mon 12 Oct", "6", "Anouk de Vries 06:00-14:30"),
                second => second.Should().Equal("Sat 17 Oct", "5", string.Empty),
                third => third.Should().Equal("Tue 13 Oct", "6", "Anouk de Vries 05:00-13:00 (edited); Joost Bakker 07:00-15:30")));
    }

    [Fact]
    public void Render_DaysTab_WithNoOpenDays_SaysSo()
    {
        Queries.ListPlanningDaysAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<DaysTab>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("There are no open days in the Planning horizon yet."));
    }

    [Fact]
    public void Render_DaysTab_WhenADayOpensWhileTheTabIsOpen_ShowsItWithoutARefresh()
    {
        var monday = new PlanningDayView(new DateOnly(2026, 10, 12), 6, []);
        Queries.ListPlanningDaysAsync(Arg.Any<CancellationToken>()).Returns([monday], [monday, new PlanningDayView(new DateOnly(2026, 10, 13), 6, [])]);
        var page = Render<DaysTab>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Mon 12 Oct"));

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Markup.Should().Contain("Tue 13 Oct"));
    }
}

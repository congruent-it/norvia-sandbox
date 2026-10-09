using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.ListDrivers;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet;

/// <summary>The licence-cover rule as the TMS query side shows it: each Driver with the Vehicle types their licences cover.</summary>
public sealed class ListDriversQueryTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly DrivingHoursLeft Full = new(TimeSpan.FromHours(9), TimeSpan.FromHours(56), TimeSpan.FromHours(90), 2);

    private readonly IDriverDirectory _drivers = Substitute.For<IDriverDirectory>();
    private readonly IPlannedDrivingHours _plannedDriving = Substitute.For<IPlannedDrivingHours>();
    // Wednesday 14 October 2026, 10:00 in Amsterdam.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));

    public ListDriversQueryTests() =>
        _plannedDriving.ListAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);

    private ListDriversHandler Handler(DrivingHourLimits? limits = null) =>
        new(_drivers, _plannedDriving, limits ?? DrivingHourLimits.Default, new PlanningHorizonRules(14, Amsterdam), _time);

    [Fact]
    public async Task Handle_ActiveDrivers_ListsEachWithTheVehicleTypesTheirLicencesCover()
    {
        _drivers.ListActive().Returns(
        [
            Driver("DRV-0001", "Anouk de Vries", LicenceCategory.CE, LicenceCategory.B),
            Driver("DRV-0004", "Bram Visser", LicenceCategory.C),
            Driver("DRV-0006", "Cor Smit", LicenceCategory.B),
        ]);

        var drivers = await Handler().Handle(new ListDriversQuery(), CancellationToken.None);

        drivers.Should().BeEquivalentTo(
            [
                new DriverView("DRV-0001", "Anouk de Vries", [LicenceCategory.B, LicenceCategory.CE], [VehicleType.Van, VehicleType.RigidTruck, VehicleType.TractorWithTrailer], Full),
                new DriverView("DRV-0004", "Bram Visser", [LicenceCategory.C], [VehicleType.Van, VehicleType.RigidTruck], Full),
                new DriverView("DRV-0006", "Cor Smit", [LicenceCategory.B], [VehicleType.Van], Full),
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_NoActiveDrivers_ListsNothing()
    {
        _drivers.ListActive().Returns([]);

        var drivers = await Handler().Handle(new ListDriversQuery(), CancellationToken.None);

        drivers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DriversWithTrips_ShowTheirOwnBudgetFromTheTripDurations()
    {
        _drivers.ListActive().Returns([Driver("DRV-0001", "Anouk de Vries", LicenceCategory.B), Driver("DRV-0006", "Cor Smit", LicenceCategory.B)]);
        _plannedDriving.ListAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20), Arg.Any<CancellationToken>()).Returns(
        [
            new PlannedDriving(new DriverId("DRV-0001"), new DateOnly(2026, 10, 14), TimeSpan.FromHours(9.5)),
            new PlannedDriving(new DriverId("DRV-0001"), new DateOnly(2026, 10, 11), TimeSpan.FromHours(6)),
        ]);

        var drivers = await Handler().Handle(new ListDriversQuery(), CancellationToken.None);

        drivers.Select(driver => driver.DrivingHours).Should().Equal(
            new DrivingHoursLeft(TimeSpan.FromHours(0.5), TimeSpan.FromHours(46.5), TimeSpan.FromHours(74.5), 1),
            Full);
    }

    [Fact]
    public async Task Handle_TheLimitsComeFromTheSettings()
    {
        _drivers.ListActive().Returns([Driver("DRV-0001", "Anouk de Vries", LicenceCategory.B)]);
        var tight = new DrivingHourLimits(TimeSpan.FromHours(4), TimeSpan.FromHours(5), 1, TimeSpan.FromHours(10), TimeSpan.FromHours(15));

        var drivers = await Handler(tight).Handle(new ListDriversQuery(), CancellationToken.None);

        drivers.Single().DrivingHours.Should().Be(new DrivingHoursLeft(TimeSpan.FromHours(4), TimeSpan.FromHours(10), TimeSpan.FromHours(15), 1));
    }

    private static Driver Driver(string id, string name, params LicenceCategory[] licences) =>
        new(new DriverId(id), name, licences.ToHashSet(), Active: true);
}

using Microsoft.Extensions.Options;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.Fleet;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

public sealed class SeedFileFleetDirectoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"norvia-fleet-seed-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void ListActive_Vehicles_AreTheActiveOnesByRegistration()
    {
        var vehicles = new SeedFileVehicleDirectory(Seed("""
            { "vehicles": [
              { "id": "VEH-0002", "registration": "RT-202-D", "type": "RigidTruck", "payloadKg": 10000, "palletPlaces": 18, "active": true },
              { "id": "VEH-0009", "registration": "ZZ-999-Z", "type": "Van", "payloadKg": 1200, "palletPlaces": 4, "active": false },
              { "id": "VEH-0001", "registration": "RT-201-C", "type": "TractorWithTrailer", "payloadKg": 24000, "palletPlaces": 33, "active": true }
            ] }
            """));

        vehicles.ListActive().Should().Equal(
            new Vehicle(new VehicleId("VEH-0001"), "RT-201-C", VehicleType.TractorWithTrailer, 24000, 33, true),
            new Vehicle(new VehicleId("VEH-0002"), "RT-202-D", VehicleType.RigidTruck, 10000, 18, true));
    }

    [Fact]
    public void ListActive_Drivers_AreTheActiveOnesByNameWithTheirLicences()
    {
        var drivers = new SeedFileDriverDirectory(Seed("""
            { "drivers": [
              { "id": "DRV-0002", "name": "Bram Visser", "licences": ["B", "C"], "active": true },
              { "id": "DRV-0009", "name": "Former Driver", "licences": ["B"], "active": false },
              { "id": "DRV-0001", "name": "Anouk de Vries", "licences": ["CE"], "active": true }
            ] }
            """));

        drivers.ListActive().Select(driver => (driver.Id.Value, driver.Name)).Should().Equal(("DRV-0001", "Anouk de Vries"), ("DRV-0002", "Bram Visser"));
        drivers.ListActive().Select(driver => driver.Licences.Order()).Should().SatisfyRespectively(
            first => first.Should().Equal(LicenceCategory.CE),
            second => second.Should().Equal(LicenceCategory.B, LicenceCategory.C));
    }

    [Fact]
    public void Find_Vehicle_AnswersInactiveOnesToo_AtRevisionOne()
    {
        var vehicles = new SeedFileVehicleDirectory(Seed("""
            { "vehicles": [
              { "id": "VEH-0009", "registration": "ZZ-999-Z", "type": "Van", "payloadKg": 1200, "palletPlaces": 4, "active": false }
            ] }
            """));

        vehicles.Find(new VehicleId("VEH-0009")).Should().Be(new Vehicle(new VehicleId("VEH-0009"), "ZZ-999-Z", VehicleType.Van, 1200, 4, false, 1));
        vehicles.Find(new VehicleId("VEH-0001")).Should().BeNull();
    }

    [Fact]
    public void Find_Driver_AnswersInactiveOnesToo_AtRevisionOne()
    {
        var drivers = new SeedFileDriverDirectory(Seed("""
            { "drivers": [
              { "id": "DRV-0009", "name": "Former Driver", "licences": ["B"], "active": false }
            ] }
            """));

        var driver = drivers.Find(new DriverId("DRV-0009"))!;

        driver.Name.Should().Be("Former Driver");
        driver.Active.Should().BeFalse();
        driver.Revision.Should().Be(1);
        driver.Licences.Should().Equal(LicenceCategory.B);
        drivers.Find(new DriverId("DRV-0001")).Should().BeNull();
    }

    [Fact]
    public void List_Rosters_AreTheWeekdaysEachDriverWorks()
    {
        var rosters = new SeedFileRosterDirectory(Seed("""
            { "drivers": [
              { "id": "DRV-0001", "name": "Anouk", "licences": ["B"], "active": true,
                "roster": { "Monday": { "start": "06:00", "end": "14:30" }, "Saturday": { "start": "08:00", "end": "12:00" } } },
              { "id": "DRV-0002", "name": "Joost", "licences": ["B"], "active": true }
            ] }
            """)).List();

        var roster = rosters.Should().ContainSingle("a Driver without a Roster has none").Subject;
        roster.DriverId.Should().Be(new DriverId("DRV-0001"));
        roster.ShiftOn(new DateOnly(2026, 10, 12)).Should().Be(new ShiftTimes(new TimeOnly(6, 0), new TimeOnly(14, 30)));
        roster.ShiftOn(new DateOnly(2026, 10, 17)).Should().Be(new ShiftTimes(new TimeOnly(8, 0), new TimeOnly(12, 0)));
        roster.ShiftOn(new DateOnly(2026, 10, 13)).Should().BeNull("Tuesday is a day off");
    }

    [Fact]
    public void List_TheDemoSeed_GivesEveryDriverAMondayToFridayRoster()
    {
        var options = Options.Create(new SeedOptions { Path = Path.Combine(RepositoryRoot(), "seed", "norvia.seed.json") });

        var rosters = new SeedFileRosterDirectory(options).List();

        rosters.Should().HaveCount(7);
        rosters.Should().OnlyContain(roster => roster.Weekdays.Keys.Order().SequenceEqual(new[]
            { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }));
        rosters.Select(roster => roster.ShiftOn(new DateOnly(2026, 10, 12))).Distinct().Should().HaveCountGreaterThan(1, "the Shift times differ");
    }

    [Fact]
    public void ListActive_SeedWithoutFleet_IsEmpty()
    {
        var options = Seed("""{ "carriers": [] }""");

        new SeedFileVehicleDirectory(options).ListActive().Should().BeEmpty();
        new SeedFileDriverDirectory(options).ListActive().Should().BeEmpty();
        new SeedFileRosterDirectory(options).List().Should().BeEmpty();
    }

    [Fact]
    public void ListActive_TheDemoSeed_HasTheOwnFleetTheDesignNames()
    {
        var options = Options.Create(new SeedOptions { Path = Path.Combine(RepositoryRoot(), "seed", "norvia.seed.json") });

        var vehicles = new SeedFileVehicleDirectory(options).ListActive();
        var drivers = new SeedFileDriverDirectory(options).ListActive();

        vehicles.GroupBy(vehicle => vehicle.Type).ToDictionary(group => group.Key, group => (group.Count(), group.Select(v => (v.PayloadKg, v.PalletPlaces)).Distinct().Single()))
            .Should().Equal(new Dictionary<VehicleType, (int, (int, int))>
            {
                [VehicleType.Van] = (2, (1200, 4)),
                [VehicleType.RigidTruck] = (3, (10000, 18)),
                [VehicleType.TractorWithTrailer] = (1, (24000, 33)),
            });
        drivers.GroupBy(driver => driver.Licences.Max()).ToDictionary(group => group.Key, group => group.Count())
            .Should().Equal(new Dictionary<LicenceCategory, int> { [LicenceCategory.CE] = 2, [LicenceCategory.C] = 3, [LicenceCategory.B] = 2 });
    }

    private IOptions<SeedOptions> Seed(string json)
    {
        File.WriteAllText(_path, json);
        return Options.Create(new SeedOptions { Path = _path });
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Norvia.slnx not found above the test output.");
    }
}

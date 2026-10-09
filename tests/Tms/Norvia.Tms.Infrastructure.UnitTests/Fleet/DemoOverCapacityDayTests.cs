using Microsoft.Extensions.Options;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Fleet;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

/// <summary>
/// The demo's Over capacity day (P10) as the repo's seed file gives it: whichever day the demo starts, the seed lands on a
/// weekday in the first week, and that day's demand is more than the Own fleet can carry once the Off the road Vehicle and the
/// Driver on leave are taken out, by the TMS's own load check.
/// </summary>
public sealed class DemoOverCapacityDayTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly IOptions<SeedOptions> Seed = Options.Create(new SeedOptions { Path = Path.Combine(AppContext.BaseDirectory, "DemoSeed", "norvia.seed.json") });

    private readonly DemoOverCapacityDay _demo = DemoSeed.Read(Seed).OverCapacityDay!;

    [Fact]
    public void Read_WhenTheSeedHasNoOverCapacityDay_GivesNone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"norvia-demo-seed-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "vehicles": [] }""");
        try
        {
            DemoSeed.Read(Options.Create(new SeedOptions { Path = path })).OverCapacityDay.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-07")] // a Monday start: Wednesday
    [InlineData("2026-10-09", "2026-10-12")] // a Friday start: Sunday, so Monday
    [InlineData("2026-10-10", "2026-10-12")] // a Saturday start: Monday
    [InlineData("2026-10-11", "2026-10-13")] // a Sunday start: Tuesday
    public void DayFor_IsTheFirstWeekdayFromTheFirstDayOnward(string start, string expected)
    {
        _demo.DayFor(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DayFor_FromAnyStartingDay_LiesWithinTheFirstWeek()
    {
        var start = new DateOnly(2026, 10, 5);

        for (var offset = 0; offset < 14; offset++)
        {
            var day = _demo.DayFor(start.AddDays(offset));
            day.DayNumber.Should().BeInRange(start.AddDays(offset).DayNumber, start.AddDays(offset + 6).DayNumber);
            day.DayOfWeek.Should().NotBe(DayOfWeek.Saturday).And.NotBe(DayOfWeek.Sunday);
        }
    }

    [Fact]
    public void BookingFor_CollectsFromTheShippersWarehouseOnTheDay_InAmsterdamTime()
    {
        var day = new DateOnly(2026, 10, 27); // after the clocks go back, so Amsterdam is UTC+1

        var booking = _demo.BookingFor(day, _demo.TransportOrders[0], Amsterdam);

        booking.ShipperId.Should().Be("SHP-0001");
        booking.ShipperReference.Should().Be("DEMO-OC-1-20261027");
        booking.Collection!.Address!.City.Should().Be("Rotterdam");
        booking.Collection.Window!.Earliest.Should().Be(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.FromHours(1)));
        booking.Collection.Window.Latest.Should().Be(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(1)));
        booking.Delivery!.Consignee!.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BookingFor_EveryDemoOrder_IsOneOfPalletsWithinTheWeightLimitOfATransportOrder()
    {
        foreach (var order in _demo.TransportOrders)
        {
            var goods = _demo.BookingFor(new DateOnly(2026, 10, 7), order, Amsterdam).GoodsLines!.Should().ContainSingle().Subject;

            goods.PackageType.Should().Be("pallet");
            goods.WeightKg.Should().BeInRange(1m, 24_000m, "the TMS rejects a Transport Order over 24 t");
        }
    }

    [Fact]
    public void TheSeededDay_WithTheOffTheRoadVehicleAndTheDriverOnLeave_IsOverCapacity()
    {
        var vehicles = new SeedFileVehicleDirectory(Seed).ListActive();
        var drivers = new SeedFileDriverDirectory(Seed).ListActive();
        var day = _demo.DayFor(new DateOnly(2026, 10, 7));

        var capacity = LoadCheck.CapacityOf(
            vehicles.Where(vehicle => vehicle.Id != _demo.VehicleId),
            drivers.Where(driver => driver.Id != _demo.DriverId));
        var demand = LoadCheck.Sum(_demo.TransportOrders.Select(order =>
            LoadCheck.DemandOf(_demo.BookingFor(day, order, Amsterdam).ToDetails())));

        demand.Exceeds(capacity).Should().BeTrue($"the day carries {demand.PalletPlaces} pallet places against a capacity of {capacity.PalletPlaces}");
    }

    [Fact]
    public void TheSeededDay_BeforeTheOffTheRoadVehicleAndTheLeave_IsNotOverCapacity()
    {
        var vehicles = new SeedFileVehicleDirectory(Seed).ListActive();
        var drivers = new SeedFileDriverDirectory(Seed).ListActive();
        var day = _demo.DayFor(new DateOnly(2026, 10, 7));

        var capacity = LoadCheck.CapacityOf(vehicles, drivers);
        var demand = LoadCheck.Sum(_demo.TransportOrders.Select(order =>
            LoadCheck.DemandOf(_demo.BookingFor(day, order, Amsterdam).ToDetails())));

        demand.Exceeds(capacity).Should().BeFalse("it is the Off the road truck that tips the day over, so putting it back is the Planner's way out");
    }

    [Fact]
    public void TheSeedNamesAVehicleAndADriverTheFleetHas_AndAReasonEachCanBeRecordedWith()
    {
        new SeedFileVehicleDirectory(Seed).ListActive().Should().Contain(vehicle => vehicle.Id == _demo.VehicleId && vehicle.Type == VehicleType.RigidTruck);
        new SeedFileDriverDirectory(Seed).ListActive().Should().Contain(driver => driver.Id == _demo.DriverId && driver.Licences.Contains(LicenceCategory.CE));
        Enum.TryParse<OffTheRoadReason>(_demo.OffTheRoad.Reason, out _).Should().BeTrue();
        Enum.TryParse<AbsenceReason>(_demo.Absence.Reason, out _).Should().BeTrue();
    }

    [Fact]
    public void BookingFor_TheSeededOrders_PassTheTmsIntakeRules()
    {
        var rules = new TransportOrderRules(24_000m);
        var shipper = new Norvia.Tms.Domain.Shippers.Shipper(new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"), "Brouwer Dranken", true);

        foreach (var order in _demo.TransportOrders)
        {
            var details = _demo.BookingFor(new DateOnly(2026, 10, 7), order, Amsterdam).ToDetails();

            rules.Check(shipper, details, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(2))).Should().BeEmpty(order.ShipperReference);
        }
    }
}

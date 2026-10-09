using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Fleet.Planning.ListOpenDays;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class ListOpenPlanningDaysTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Fact]
    public async Task Handle_OpenAndClosedDays_ListsOneRowPerOpenDay_WithShiftsByStartThenName()
    {
        var anouk = new DriverId("DRV-0001");
        var joost = new DriverId("DRV-0002");
        var roster = (DriverId id, int hour) => new Roster(id, new Dictionary<DayOfWeek, ShiftTimes> { [DayOfWeek.Monday] = new(new TimeOnly(hour, 0), new TimeOnly(hour + 8, 0)) });
        var closed = PlanningDay.Open(Monday.AddDays(-7), [roster(anouk, 6)], []);
        closed.Close();
        var stored = new List<PlanningDay> { closed, PlanningDay.Open(Monday, [roster(joost, 6), roster(anouk, 6)], [new VehicleId("VEH-0001"), new VehicleId("VEH-0002")]) };
        var days = Substitute.For<IReadRepository<PlanningDay>>();
        days.ListAsync(Arg.Any<ISpecification<PlanningDay>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<PlanningDay>>().Evaluate(stored).ToList());
        var drivers = Substitute.For<IDriverDirectory>();
        drivers.ListActive().Returns(
        [
            new Driver(anouk, "Anouk de Vries", new HashSet<LicenceCategory> { LicenceCategory.B }, true),
            new Driver(joost, "Joost Bakker", new HashSet<LicenceCategory> { LicenceCategory.B }, true),
        ]);
        var vehicles = Substitute.For<IVehicleDirectory>();
        vehicles.ListActive().Returns([Vehicle("VEH-0001", VehicleType.Van, 1200, 4), Vehicle("VEH-0002", VehicleType.Van, 1200, 4)]);

        var listed = await Handler(days, drivers, vehicles, [], []).Handle(new ListOpenPlanningDaysQuery(), CancellationToken.None);

        var day = listed.Should().ContainSingle().Subject;
        day.Date.Should().Be(Monday);
        day.AvailableVehicles.Should().Be(2);
        day.Shifts.Select(shift => shift.DriverName).Should().Equal("Anouk de Vries", "Joost Bakker");
        day.Shifts[0].Should().Be(new ShiftView("DRV-0001", "Anouk de Vries", new TimeOnly(6, 0), new TimeOnly(14, 0), false));
    }

    [Fact]
    public async Task Handle_PairsVehiclesWithDrivers_AndCountsOnlyPairedVehicles()
    {
        var driver = new DriverId("DRV-0001");
        var day = PlanningDay.Open(Monday, [WeekdayRoster(driver)], [new VehicleId("VEH-0001"), new VehicleId("VEH-0002")]);

        var listed = await Handler(day, [Driver(driver, LicenceCategory.C)], [Vehicle("VEH-0001", VehicleType.RigidTruck, 10000, 18), Vehicle("VEH-0002", VehicleType.Van, 1200, 4)], [], [])
            .Handle(new ListOpenPlanningDaysQuery(), CancellationToken.None);

        listed.Single().Load.Should().Be(new DayLoadView(10000, 18, 0, 0, false));
    }

    [Fact]
    public async Task Handle_ADriverWithAnAbsence_OrAVehicleOffTheRoad_IsLeftOutOfTheCapacity()
    {
        var driver = new DriverId("DRV-0001");
        var other = new DriverId("DRV-0002");
        var absent = PlanningDay.Open(Monday, [WeekdayRoster(driver), WeekdayRoster(other)], [new VehicleId("VEH-0001")], new HashSet<DriverId> { driver });
        var offTheRoad = PlanningDay.Open(Monday, [WeekdayRoster(driver)], [new VehicleId("VEH-0001")], null, new HashSet<VehicleId> { new("VEH-0001") });
        var drivers = new[] { Driver(driver, LicenceCategory.C), Driver(other, LicenceCategory.B) };
        var vehicles = new[] { Vehicle("VEH-0001", VehicleType.RigidTruck, 10000, 18) };

        var withAbsence = await Handler(absent, drivers, vehicles, [], []).Handle(new ListOpenPlanningDaysQuery(), CancellationToken.None);
        var withOffTheRoad = await Handler(offTheRoad, drivers, vehicles, [], []).Handle(new ListOpenPlanningDaysQuery(), CancellationToken.None);

        withAbsence.Single().Load.CapacityKg.Should().Be(0);
        withOffTheRoad.Single().Load.CapacityKg.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Demand_CountsPlannedAndReleasedOwnFleetShipmentsOnTheAmsterdamDayOfTheEarliestCollectionTime()
    {
        var driver = new DriverId("DRV-0001");
        var monday = PlanningDay.Open(Monday, [WeekdayRoster(driver)], [new VehicleId("VEH-0001")]);
        var tuesday = PlanningDay.Open(Monday.AddDays(1), [WeekdayRoster(driver)], [new VehicleId("VEH-0001")]);
        // 22:30 UTC on Sunday 11 Oct is 00:30 on Monday 12 Oct in Amsterdam (CEST); 21:30 UTC on Monday is 23:30 on Monday.
        var orders = new List<TransportOrder>
        {
            Order(1, new DateTimeOffset(2026, 10, 11, 22, 30, 0, TimeSpan.Zero), new GoodsLine("pallet", 3, 900m, null), new GoodsLine("parcel", 10, 100m, null)),
            Order(2, new DateTimeOffset(2026, 10, 12, 21, 30, 0, TimeSpan.Zero), new GoodsLine("pallet", 2, 400m, null)),
            Order(3, new DateTimeOffset(2026, 10, 12, 22, 30, 0, TimeSpan.Zero), new GoodsLine("pallet", 5, 2000m, null)),
            Order(4, new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero), new GoodsLine("pallet", 9, 9000m, null)),
        };
        var shipments = new List<Shipment>
        {
            Samples.Planned(orders[0], CarriedBy.OwnFleet, sequence: 1),
            ReleasedOwnFleet(orders[1], sequence: 2),
            Samples.Planned(orders[2], CarriedBy.OwnFleet, sequence: 3),
            Samples.Planned(orders[3], CarriedBy.Carrier(new Norvia.Tms.Domain.Carriers.CarrierId("CAR-0001")), sequence: 4),
        };

        var listed = await Handler([monday, tuesday], [Driver(driver, LicenceCategory.C)], [Vehicle("VEH-0001", VehicleType.RigidTruck, 10000, 18)], orders, shipments)
            .Handle(new ListOpenPlanningDaysQuery(), CancellationToken.None);

        listed[0].Load.Should().Be(new DayLoadView(10000, 18, 1400, 5, false));
        listed[1].Load.Should().Be(new DayLoadView(10000, 18, 2000, 5, false));
    }

    [Theory]
    [InlineData(10001, 18, true)]
    [InlineData(10000, 19, true)]
    [InlineData(10000, 18, false)]
    public async Task Handle_Demand_IsOverCapacityOnWeightAloneAndOnPalletPlacesAlone(int kg, int pallets, bool over)
    {
        var driver = new DriverId("DRV-0001");
        var day = PlanningDay.Open(Monday, [WeekdayRoster(driver)], [new VehicleId("VEH-0001")]);
        var order = Order(1, new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), new GoodsLine("pallet", pallets, kg, null));

        var listed = await Handler(day, [Driver(driver, LicenceCategory.C)], [Vehicle("VEH-0001", VehicleType.RigidTruck, 10000, 18)], [order], [Samples.Planned(order, CarriedBy.OwnFleet, sequence: 1)])
            .Handle(new ListOpenPlanningDaysQuery(), CancellationToken.None);

        listed.Single().Load.Over.Should().Be(over);
    }

    private static ListOpenPlanningDaysHandler Handler(
        PlanningDay day, Driver[] drivers, Vehicle[] vehicles, List<TransportOrder> orders, List<Shipment> shipments) =>
        Handler([day], drivers, vehicles, orders, shipments);

    private static ListOpenPlanningDaysHandler Handler(
        List<PlanningDay> stored, Driver[] drivers, Vehicle[] vehicles, List<TransportOrder> orders, List<Shipment> shipments)
    {
        var directory = Substitute.For<IDriverDirectory>();
        directory.ListActive().Returns(drivers);
        var fleet = Substitute.For<IVehicleDirectory>();
        fleet.ListActive().Returns(vehicles);
        return Handler(Repository(stored), directory, fleet, orders, shipments);
    }

    private static ListOpenPlanningDaysHandler Handler(
        IReadRepository<PlanningDay> days, IDriverDirectory drivers, IVehicleDirectory vehicles, List<TransportOrder> orders, List<Shipment> shipments) =>
        new(days, Repository(shipments), Repository(orders), drivers, vehicles);

    private static IReadRepository<T> Repository<T>(List<T> stored)
        where T : class, IAggregateRoot
    {
        var repository = Substitute.For<IReadRepository<T>>();
        repository.ListAsync(Arg.Any<ISpecification<T>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<T>>().Evaluate(stored).ToList());
        return repository;
    }

    private static Roster WeekdayRoster(DriverId driver) => new(
        driver,
        Enum.GetValues<DayOfWeek>().ToDictionary(weekday => weekday, _ => new ShiftTimes(new TimeOnly(6, 0), new TimeOnly(14, 0))));

    private static Driver Driver(DriverId id, LicenceCategory licence) => new(id, id.Value, new HashSet<LicenceCategory> { licence }, true);

    private static Vehicle Vehicle(string id, VehicleType type, int payloadKg, int pallets) => new(new VehicleId(id), id, type, payloadKg, pallets, true);

    private static TransportOrder Order(long sequence, DateTimeOffset collectsAt, params GoodsLine[] lines)
    {
        var details = Samples.CanonicalTransportOrder().ToDetails() with
        {
            Collection = new CollectionDetails(null, new TimeWindow(collectsAt, collectsAt.AddHours(4))),
            GoodsLines = lines,
        };
        return TransportOrder.Accept(
            new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"), details, TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId($"corr-{sequence}"), Samples.Now);
    }

    private static Shipment ReleasedOwnFleet(TransportOrder order, long sequence)
    {
        var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence);
        shipment.Release(order, new IdempotencyKey($"key-rel{sequence}"), new CorrelationId("corr-release"), Samples.Now.AddHours(3));
        return shipment;
    }
}

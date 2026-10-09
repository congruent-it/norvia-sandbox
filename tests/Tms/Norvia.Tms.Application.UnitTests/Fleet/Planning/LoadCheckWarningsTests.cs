using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class LoadCheckWarningsTests
{
    // Thursday 24 Sep 2026: the horizon runs to Thursday 8 Oct.
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private static readonly DateOnly Thursday = new(2026, 10, 1);
    private static readonly DriverId Anouk = new("DRV-0001");

    [Fact]
    public async Task For_OwnFleetShipmentThatMakesTheDayOverCapacity_WarnsWithTheShipmentCounted()
    {
        var order = Order(1, Thursday, new GoodsLine("pallet", 34, 3400m, null));
        var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 1);

        var warning = await Warnings([order], [shipment]).ForAsync(shipment, CancellationToken.None);

        warning.Should().Be(new LoadCheckWarning(Thursday, OutsideHorizon: false, new DayLoadView(30000, 30, 3400, 34, Over: true)));
        warning!.Text.Should().Be("Thu 1 Oct: Over capacity, 34 of 30 pallet places after this assignment");
    }

    [Fact]
    public async Task For_AShipmentOverOnWeightAndPalletPlaces_NamesBoth()
    {
        var order = Order(1, Thursday, new GoodsLine("pallet", 31, 31000m, null));
        var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 1);

        var warning = await Warnings([order], [shipment]).ForAsync(shipment, CancellationToken.None);

        warning!.Text.Should().Be("Thu 1 Oct: Over capacity, 31000 of 30000 kg and 31 of 30 pallet places after this assignment");
    }

    [Fact]
    public async Task For_AShipmentThatFitsTheDay_HasNoWarning()
    {
        var order = Order(1, Thursday, new GoodsLine("pallet", 30, 3000m, null));
        var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 1);

        (await Warnings([order], [shipment]).ForAsync(shipment, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task For_ACollectionDayBeyondTheHorizon_SaysItIsOutsideThePlanningHorizon()
    {
        var order = Order(1, new DateOnly(2026, 10, 9), new GoodsLine("pallet", 99, 99000m, null));
        var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 1);

        var warning = await Warnings([order], [shipment]).ForAsync(shipment, CancellationToken.None);

        warning.Should().Be(new LoadCheckWarning(new DateOnly(2026, 10, 9), OutsideHorizon: true, Load: null));
        warning!.Text.Should().Be("Fri 9 Oct: Outside the Planning horizon");
    }

    [Fact]
    public async Task For_AShipmentCarriedByACarrier_HasNoWarning()
    {
        var order = Order(1, Thursday, new GoodsLine("pallet", 99, 99000m, null));
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new("CAR-0001")), sequence: 1);

        (await Warnings([order], [shipment]).ForAsync(shipment, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task For_ADayThatIsNotOpenYet_HasNoWarning()
    {
        var order = Order(1, Thursday, new GoodsLine("pallet", 99, 99000m, null));
        var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 1);

        var warnings = LoadChecks.Warnings([], [order], [shipment], [], [], _time);

        (await warnings.ForAsync(shipment, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task For_AFailureWorkingItOut_GivesNoWarning_SoTheSavedAssignmentIsntLost()
    {
        var order = Order(1, Thursday, new GoodsLine("pallet", 99, 99000m, null));
        var shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 1);
        var days = Substitute.For<IReadRepository<PlanningDay>>();
        days.FirstOrDefaultAsync(Arg.Any<ISpecification<PlanningDay>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));
        var warnings = new LoadCheckWarnings(
            days,
            LoadChecks.Repository([order]),
            LoadChecks.LoadsOver([shipment], [order], Substitute.For<IDriverDirectory>(), Substitute.For<IVehicleDirectory>()),
            new PlanningHorizonRules(14, LoadChecks.Amsterdam),
            _time,
            NullLogger<LoadCheckWarnings>.Instance);

        (await warnings.ForAsync(shipment, CancellationToken.None)).Should().BeNull();
    }

    private LoadCheckWarnings Warnings(List<TransportOrder> orders, List<Shipment> shipments)
    {
        var roster = new Roster(Anouk, Enum.GetValues<DayOfWeek>().ToDictionary(weekday => weekday, _ => new ShiftTimes(new TimeOnly(6, 0), new TimeOnly(14, 0))));
        var day = PlanningDay.Open(Thursday, [roster], [new VehicleId("VEH-0001")]);
        return LoadChecks.Warnings(
            [day],
            orders,
            shipments,
            [new Driver(Anouk, "Anouk de Vries", new HashSet<LicenceCategory> { LicenceCategory.C }, true)],
            [new Vehicle(new VehicleId("VEH-0001"), "VEH-0001", VehicleType.RigidTruck, 30000, 30, true)],
            _time);
    }

    private static TransportOrder Order(long sequence, DateOnly collectsOn, params GoodsLine[] lines)
    {
        // Midday UTC is the same date in Amsterdam.
        var collectsAt = new DateTimeOffset(collectsOn.Year, collectsOn.Month, collectsOn.Day, 12, 0, 0, TimeSpan.Zero);
        var details = Samples.CanonicalTransportOrder().ToDetails() with
        {
            Collection = new CollectionDetails(null, new TimeWindow(collectsAt, collectsAt.AddHours(4))),
            GoodsLines = lines,
        };
        return TransportOrder.Accept(
            new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"), details, TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId($"corr-{sequence}"), Samples.Now);
    }
}

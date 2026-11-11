using Ardalis.Result;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

/// <summary>
/// The Planner agent's one tool (#440): the options reuse the Assignment rules, the load check with the order counted, the Driving-hour budget
/// and the Carrier Rate Card's pricing that P3, P10 and P9 already run.
/// </summary>
public sealed class GetAssignmentOptionsHandlerTests
{
    // Thursday 24 Sep 2026: the horizon runs to Thursday 8 Oct.
    private static readonly DateOnly Friday = new(2026, 9, 25);
    private static readonly DriverId Anouk = new("DRV-0001");
    private static readonly VehicleId Truck = new("VEH-0001");
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CarrierId Kessler = new("CAR-0002");

    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly List<PlanningDay> _days = [];
    private readonly List<TransportOrder> _orders = [];
    private readonly List<Shipment> _shipments = [];
    private readonly List<PlannedDriving> _planned = [];
    private readonly List<Carrier> _activeCarriers = [new(VanDijk, "Van Dijk Transport", true), new(Kessler, "Kessler Spedition", true)];
    private readonly Dictionary<CarrierId, CarrierRateCard> _rateCards = new()
    {
        [VanDijk] = RateCardOf(VanDijk, "Utrecht", 12.40m),
        [Kessler] = RateCardOf(Kessler, "Utrecht", 9.90m),
    };

    private readonly Driver[] _drivers = [new(Anouk, "Anouk de Vries", new HashSet<LicenceCategory> { LicenceCategory.C }, true)];
    private readonly Vehicle[] _vehicles = [new(Truck, "NL-01-TR", VehicleType.RigidTruck, 30000, 30, true)];

    public GetAssignmentOptionsHandlerTests()
    {
        _days.Add(PlanningDay.Open(Friday, [Roster()], [Truck]));
    }

    [Fact]
    public async Task Handle_AnOrderThatFitsOwnFleet_SaysItFits_WithTheCapacityTheDriversAndTheDrivingHoursLeft_AndPricesEveryActiveCarrier()
    {
        var order = Order(Friday, new GoodsLine("pallet", 2, 850m, null), new GoodsLine("crate", 4, 60.5m, null));

        var options = (await HandleAsync(order)).Value;

        options.TransportOrderNumber.Should().Be("TO-0000007");
        options.TotalWeightKg.Should().Be(910.5m);
        options.PalletPlaces.Should().Be(2);
        options.ShipperName.Should().Be("Brouwer Dranken");
        options.OwnFleet.Should().Be(new OwnFleetOption(
            Refusal: null,
            OwnFleetFit.Fits,
            Friday,
            Warning: null,
            new Norvia.Tms.Application.Fleet.Planning.DayLoadView(30000, 30, 910.5m, 2, Over: false),
            AvailableVehicles: 1,
            AvailableDrivers: 1,
            DriversWithDrivingHoursLeft: 1,
            MostDrivingHoursLeft: TimeSpan.FromHours(9)));
        options.Carriers.Should().BeEquivalentTo(
            [
                new CarrierOption("CAR-0001", "Van Dijk Transport", null, 112.90m, null),
                new CarrierOption("CAR-0002", "Kessler Spedition", null, 90.14m, null),
            ]);
    }

    [Fact]
    public async Task Handle_AnOrderThatMakesTheDayOverCapacity_NamesTheWarning_AndStillOffersEveryOption()
    {
        var order = Order(Friday, new GoodsLine("pallet", 34, 3400m, null));

        var own = (await HandleAsync(order)).Value.OwnFleet;

        own.Fit.Should().Be(OwnFleetFit.OverCapacity);
        own.Refusal.Should().BeNull("Over capacity is a warning and never blocks an assignment (P10)");
        own.Warning!.Text.Should().Be("Fri 25 Sep: Over capacity, 34 of 30 pallet places after this assignment");
        own.Load!.Over.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OtherOwnFleetWorkOnTheDay_CountsAgainstTheOrder()
    {
        var earlier = Build(Friday, 3, withoutWindow: false, new GoodsLine("pallet", 29, 2900m, null));
        _orders.Add(earlier);
        _shipments.Add(Samples.Planned(earlier, CarriedBy.OwnFleet, sequence: 1));

        var own = (await HandleAsync(Order(Friday, new GoodsLine("pallet", 2, 200m, null)))).Value.OwnFleet;

        own.Fit.Should().Be(OwnFleetFit.OverCapacity);
        own.Load.Should().Be(new Norvia.Tms.Application.Fleet.Planning.DayLoadView(30000, 30, 3100m, 31, Over: true));
    }

    [Fact]
    public async Task Handle_ACollectionDayBeyondThePlanningHorizon_IsAnOutOfHorizonWarning_WithNoLoadCheck()
    {
        var own = (await HandleAsync(Order(new DateOnly(2026, 10, 9), new GoodsLine("pallet", 2, 200m, null)))).Value.OwnFleet;

        own.Fit.Should().Be(OwnFleetFit.OutsideHorizon);
        own.Warning!.Text.Should().Be("Fri 9 Oct: Outside the Planning horizon");
        own.Load.Should().BeNull();
        own.Refusal.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ADayThatIsNotOpen_CannotBeCarriedByOwnFleet()
    {
        _days.Clear();

        var own = (await HandleAsync(Order(Friday, new GoodsLine("pallet", 2, 200m, null)))).Value.OwnFleet;

        own.Fit.Should().Be(OwnFleetFit.DayNotOpen);
        own.AvailableDrivers.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NoDriverWithDrivingHoursLeft_IsNotAFit_EvenWhenTheLoadFits()
    {
        _planned.Add(new PlannedDriving(Anouk, Friday, TimeSpan.FromHours(9)));

        var own = (await HandleAsync(Order(Friday, new GoodsLine("pallet", 2, 200m, null)))).Value.OwnFleet;

        own.Fit.Should().Be(OwnFleetFit.NoDriverWithDrivingHoursLeft);
        own.AvailableDrivers.Should().Be(1);
        own.DriversWithDrivingHoursLeft.Should().Be(0);
        own.MostDrivingHoursLeft.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Handle_AnInactiveDriverOrVehicle_CountsForNothing()
    {
        _drivers[0] = _drivers[0] with { Active = false };

        var own = (await HandleAsync(Order(Friday, new GoodsLine("pallet", 2, 200m, null)))).Value.OwnFleet;

        own.AvailableDrivers.Should().Be(0);
        own.Fit.Should().Be(OwnFleetFit.NoDriverWithDrivingHoursLeft);
    }

    [Fact]
    public async Task Handle_ACarrierWhoseRateCardHasNoLaneForTheOrder_CannotBePriced_WithNoMatchingLane()
    {
        _rateCards[Kessler] = RateCardOf(Kessler, "Eindhoven", 9.90m);

        var carriers = (await HandleAsync(Order(Friday, new GoodsLine("pallet", 2, 200m, null)))).Value.Carriers;

        carriers.Single(carrier => carrier.CarrierId == "CAR-0002").Should().Be(
            new CarrierOption("CAR-0002", "Kessler Spedition", null, null, AssignmentRefusalCode.NoMatchingLane));
        carriers.Single(carrier => carrier.CarrierId == "CAR-0001").CarrierCost.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ACarrierWithNoCarrierRateCard_CannotBePriced()
    {
        _rateCards.Remove(Kessler);

        var carriers = (await HandleAsync(Order(Friday, new GoodsLine("pallet", 2, 200m, null)))).Value.Carriers;

        carriers.Single(carrier => carrier.CarrierId == "CAR-0002").NotPriced.Should().Be(AssignmentRefusalCode.NoRateCard);
    }

    [Fact]
    public async Task Handle_OnlyActiveCarriersAreOffered_AnInactiveOneIsNeverListed()
    {
        var carriers = (await HandleAsync(Order(Friday, new GoodsLine("pallet", 2, 200m, null)), activeCarriers: [_activeCarriers[0]])).Value.Carriers;

        carriers.Select(carrier => carrier.CarrierId).Should().Equal("CAR-0001");
    }

    [Fact]
    public async Task Handle_AnOrderThatIsPlannedAlready_BreaksTheAssignmentRuleForEveryOption()
    {
        var order = Order(Friday, new GoodsLine("pallet", 2, 200m, null));
        order.Plan(ShipmentNumber.FromSequence(1), CarriedBy.OwnFleet, new IdempotencyKey("k"), new CorrelationId("c"), Samples.Now);

        var options = (await HandleAsync(order)).Value;

        options.OwnFleet.Refusal!.Code.Should().Be(AssignmentRefusalCode.TransportOrderAlreadyPlanned);
        options.Carriers.Should().OnlyContain(carrier => carrier.Refusal!.Code == AssignmentRefusalCode.TransportOrderAlreadyPlanned);
    }

    [Fact]
    public async Task Handle_AnOrderWithNoCollectionWindow_HasNothingToCheckOwnFleetAgainst()
    {
        var order = Build(Friday, 7, withoutWindow: true, new GoodsLine("pallet", 2, 200m, null));

        (await HandleAsync(order)).Value.OwnFleet.Fit.Should().Be(OwnFleetFit.NoCollectionDay);
    }

    [Fact]
    public async Task Handle_TheOrderWithNoAddress_HasNoPlaces_AndNoStreetEverReachesTheResult()
    {
        var order = Order(Friday, new GoodsLine("pallet", 2, 200m, null));

        var options = (await HandleAsync(order)).Value;

        options.Collection.Should().Be(new Norvia.Tms.Application.TransportOrders.PlaceView("3087 BM", "Rotterdam", "NL"));
        options.Delivery.Should().Be(new Norvia.Tms.Application.TransportOrders.PlaceView("3511 AW", "Utrecht", "NL"));
    }

    [Theory]
    [InlineData("TO-0000042")]
    [InlineData("not-a-number")]
    public async Task Handle_ANumberItDoesntFind_IsNotFound(string number)
    {
        var result = await Handler().Handle(new GetAssignmentOptionsQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    private async Task<Result<AssignmentOptions>> HandleAsync(TransportOrder order, IReadOnlyList<Carrier>? activeCarriers = null)
    {
        _orders.Add(order);
        return await Handler(activeCarriers).Handle(new GetAssignmentOptionsQuery(order.Number!.Value), CancellationToken.None);
    }

    private GetAssignmentOptionsHandler Handler(IReadOnlyList<Carrier>? activeCarriers = null)
    {
        var shippers = Substitute.For<IShipperDirectory>();
        shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        var carriers = Substitute.For<ICarrierDirectory>();
        carriers.ListActive().Returns(activeCarriers ?? _activeCarriers);
        var carrierRateCards = Substitute.For<ICarrierRateCardDirectory>();
        carrierRateCards.FindFor(Arg.Any<CarrierId>()).Returns(call => _rateCards.GetValueOrDefault(call.Arg<CarrierId>()));
        var vehicles = Substitute.For<IVehicleDirectory>();
        vehicles.ListActive().Returns(_vehicles.Where(vehicle => vehicle.Active).ToList());
        var drivers = Substitute.For<IDriverDirectory>();
        drivers.ListActive().Returns(_drivers.Where(driver => driver.Active).ToList());
        var planned = Substitute.For<IPlannedDrivingHours>();
        planned.ListAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => _planned.ToList());

        return new GetAssignmentOptionsHandler(
            LoadChecks.Repository(_orders),
            LoadChecks.Repository(_days),
            shippers,
            carriers,
            carrierRateCards,
            vehicles,
            drivers,
            planned,
            DrivingHourLimits.Default,
            LoadChecks.Warnings(_days, _orders, _shipments, [.. _drivers.Where(driver => driver.Active)], [.. _vehicles.Where(vehicle => vehicle.Active)], _time),
            _time);
    }

    private static Roster Roster() =>
        new(Anouk, Enum.GetValues<DayOfWeek>().ToDictionary(weekday => weekday, _ => new ShiftTimes(new TimeOnly(6, 0), new TimeOnly(14, 0))));

    private static CarrierRateCard RateCardOf(CarrierId carrier, string destinationZone, decimal pricePer100Kg) => new(
        new RateCardId($"CRC-{carrier.Value}"),
        carrier,
        [
            new RateCardVersion(
                2,
                new DateOnly(2026, 1, 1),
                null,
                [new Zone(new ZoneName("Rotterdam"), "NL", ["30"]), new Zone(new ZoneName("Utrecht"), "NL", ["35"])],
                [new Lane(new ZoneName("Rotterdam"), new ZoneName(destinationZone), [new WeightBreak(1_000m, new Money(pricePer100Kg))], new Money(45.00m))]),
        ]);

    private static TransportOrder Order(DateOnly collectsOn, params GoodsLine[] lines) => Build(collectsOn, 7, withoutWindow: false, lines);

    private static TransportOrder Build(DateOnly collectsOn, long sequence, bool withoutWindow, params GoodsLine[] lines)
    {
        // Midday UTC is the same date in Amsterdam.
        var collectsAt = new DateTimeOffset(collectsOn.Year, collectsOn.Month, collectsOn.Day, 12, 0, 0, TimeSpan.Zero);
        var canonical = Samples.CanonicalTransportOrder().ToDetails();
        var details = canonical with
        {
            Collection = canonical.Collection! with { Window = withoutWindow ? null : new TimeWindow(collectsAt, collectsAt.AddHours(4)) },
            GoodsLines = lines,
        };
        return TransportOrder.Accept(
            new ShipperId("SHP-0001"), details, TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId($"corr-{sequence}"), Samples.Now);
    }
}

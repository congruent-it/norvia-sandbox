using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class LoadCheckTests
{
    private static readonly Vehicle Van = Vehicle("VEH-V", VehicleType.Van, 1200, 4);
    private static readonly Vehicle Truck = Vehicle("VEH-T", VehicleType.RigidTruck, 10000, 18);
    private static readonly Vehicle Tractor = Vehicle("VEH-C", VehicleType.TractorWithTrailer, 24000, 33);

    [Fact]
    public void CapacityOf_EachVehicleGetsADriver_AddsEveryPayloadAndPalletPlace()
    {
        var capacity = LoadCheck.CapacityOf([Van, Truck, Tractor], [Driver("A", LicenceCategory.B), Driver("B", LicenceCategory.C), Driver("C", LicenceCategory.CE)]);

        capacity.Should().Be(new FleetLoad(35200, 55));
    }

    [Fact]
    public void CapacityOf_TheLargestVehicleGetsTheLowestLicenceThatCoversIt_SoTheCEDriverIsKeptForTheTractor()
    {
        // One C and one CE Driver: the truck takes the C Driver, leaving the CE Driver for the tractor.
        var capacity = LoadCheck.CapacityOf([Truck, Tractor], [Driver("A", LicenceCategory.CE), Driver("B", LicenceCategory.C)]);

        capacity.Should().Be(new FleetLoad(34000, 51));
    }

    [Fact]
    public void CapacityOf_ALargeVehicleTakesTheLowestCoveringLicenceFirst_EvenWhenALowerOnePairsLater()
    {
        // Tractor first (largest): the only CE Driver. Truck then takes the C Driver. The van has none left.
        var capacity = LoadCheck.CapacityOf([Van, Truck, Tractor], [Driver("A", LicenceCategory.CE), Driver("B", LicenceCategory.C)]);

        capacity.Should().Be(new FleetLoad(34000, 51));
    }

    [Fact]
    public void CapacityOf_AVehicleWithoutADriverAddsNothing_AndEachDriverDrivesOneVehicle()
    {
        var capacity = LoadCheck.CapacityOf([Van, Truck, Tractor], [Driver("A", LicenceCategory.CE)]);

        capacity.Should().Be(new FleetLoad(24000, 33));
    }

    [Fact]
    public void CapacityOf_ADriverWhoseLicenceDoesntCoverAnyVehicle_AddsNothing()
    {
        var capacity = LoadCheck.CapacityOf([Truck, Tractor], [Driver("A", LicenceCategory.B)]);

        capacity.Should().Be(FleetLoad.None);
    }

    [Fact]
    public void CapacityOf_NoVehiclesOrNoDrivers_IsNone()
    {
        LoadCheck.CapacityOf([], [Driver("A", LicenceCategory.CE)]).Should().Be(FleetLoad.None);
        LoadCheck.CapacityOf([Van], []).Should().Be(FleetLoad.None);
    }

    [Fact]
    public void DemandOf_CountsAllWeight_AndOnlyPalletQuantitiesAsPalletPlaces()
    {
        var details = Details(
            new GoodsLine("pallet", 6, 3000m, null),
            new GoodsLine("Pallet", 2, 500m, null),
            new GoodsLine("parcel", 40, 120.5m, null),
            new GoodsLine("crate", 3, null, null));

        LoadCheck.DemandOf(details).Should().Be(new FleetLoad(3620.5m, 8));
    }

    [Theory]
    [InlineData(10000, 18, 10000, 18, false)]
    [InlineData(10000.1, 18, 10000, 18, true)]
    [InlineData(10000, 19, 10000, 18, true)]
    [InlineData(500, 2, 10000, 18, false)]
    public void Exceeds_IsTrueWhenWeightAloneOrPalletPlacesAloneAreMoreThanTheCapacity(double kg, int pallets, double capacityKg, int capacityPallets, bool over)
    {
        new FleetLoad((decimal)kg, pallets).Exceeds(new FleetLoad((decimal)capacityKg, capacityPallets)).Should().Be(over);
    }

    private static Vehicle Vehicle(string registration, VehicleType type, int payloadKg, int pallets) =>
        new(new VehicleId(registration), registration, type, payloadKg, pallets, true);

    private static Driver Driver(string name, params LicenceCategory[] licences) =>
        new(new DriverId($"DRV-{name}"), name, licences.ToHashSet(), true);

    private static TransportOrderDetails Details(params GoodsLine[] lines) => new(null, null, null, lines, null);
}

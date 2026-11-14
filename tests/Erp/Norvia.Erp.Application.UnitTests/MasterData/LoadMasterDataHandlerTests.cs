using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.MasterData.Load;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.Drivers;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Shippers;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Application.UnitTests.MasterData;

public sealed class LoadMasterDataHandlerTests
{
    private static readonly RateCardVersion[] Versions = [new(1, new DateOnly(2026, 1, 1), null, [], [])];

    private static readonly MasterDataSeed Seed = new(
        [new SeedShipper("SHP-0001", "Brouwer Dranken", true)],
        [new SeedCarrier("CAR-0001", "Van Dijk Transport", true)],
        [new SeedRateCard("RC-0001", "SHP-0001", Versions)],
        [new SeedRateCard("CRC-0001", "CAR-0001", Versions)],
        [new SeedVehicle("VEH-0001", "VN-101-A", VehicleType.Van, 1200, 4, true)],
        [new SeedDriver("DRV-0001", "Anouk de Vries", [LicenceCategory.B, LicenceCategory.CE], true)]);

    private readonly IRepository<Shipper> _shippers = Substitute.For<IRepository<Shipper>>();
    private readonly IRepository<Carrier> _carriers = Substitute.For<IRepository<Carrier>>();
    private readonly IRepository<RateCard> _rateCards = Substitute.For<IRepository<RateCard>>();
    private readonly IRepository<CarrierRateCard> _carrierRateCards = Substitute.For<IRepository<CarrierRateCard>>();
    private readonly IRepository<Vehicle> _vehicles = Substitute.For<IRepository<Vehicle>>();
    private readonly IRepository<Driver> _drivers = Substitute.For<IRepository<Driver>>();

    [Fact]
    public async Task Load_IntoAnEmptyDatabase_AddsEverythingWithTheTmsIds()
    {
        var result = await Load(Seed);

        result.Should().Be(new LoadMasterDataResult(Added: 6, Reloaded: 0));
        await _shippers.Received().AddAsync(Arg.Is<Shipper>(shipper => shipper.Id.Value == "SHP-0001" && shipper.Name == "Brouwer Dranken"), Arg.Any<CancellationToken>());
        await _carriers.Received().AddAsync(Arg.Is<Carrier>(carrier => carrier.Id.Value == "CAR-0001"), Arg.Any<CancellationToken>());
        await _rateCards.Received().AddAsync(Arg.Is<RateCard>(card => card.Id.Value == "RC-0001" && card.ShipperId.Value == "SHP-0001"), Arg.Any<CancellationToken>());
        await _carrierRateCards.Received().AddAsync(Arg.Is<CarrierRateCard>(card => card.Id.Value == "CRC-0001" && card.CarrierId.Value == "CAR-0001"), Arg.Any<CancellationToken>());
        await _vehicles.Received().AddAsync(Arg.Is<Vehicle>(vehicle => vehicle.Id.Value == "VEH-0001" && vehicle.Registration == "VN-101-A" && vehicle.PayloadKg == 1200), Arg.Any<CancellationToken>());
        await _drivers.Received().AddAsync(Arg.Is<Driver>(driver => driver.Id.Value == "DRV-0001" && driver.Licences.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_WhenEverythingIsThereAlready_ReloadsItInsteadOfAddingItAgain()
    {
        var shipper = new Shipper(new ShipperId("SHP-0001"), "Old name", active: false);
        var carrier = new Carrier(new CarrierId("CAR-0001"), "Old name", active: false);
        var rateCard = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0009"), []);
        var carrierRateCard = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0009"), []);
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns(shipper);
        _carriers.FirstOrDefaultAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns(carrier);
        _rateCards.FirstOrDefaultAsync(Arg.Any<ISpecification<RateCard>>(), Arg.Any<CancellationToken>()).Returns(rateCard);
        _carrierRateCards.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierRateCard>>(), Arg.Any<CancellationToken>()).Returns(carrierRateCard);
        var vehicle = new Vehicle(new VehicleId("VEH-0001"), "OLD", VehicleType.RigidTruck, 5000, 8, active: false);
        var driver = new Driver(new DriverId("DRV-0001"), "Old name", [], active: false);
        _vehicles.FirstOrDefaultAsync(Arg.Any<ISpecification<Vehicle>>(), Arg.Any<CancellationToken>()).Returns(vehicle);
        _drivers.FirstOrDefaultAsync(Arg.Any<ISpecification<Driver>>(), Arg.Any<CancellationToken>()).Returns(driver);

        var result = await Load(Seed);

        result.Should().Be(new LoadMasterDataResult(Added: 0, Reloaded: 6));
        shipper.Name.Should().Be("Brouwer Dranken");
        shipper.Active.Should().BeTrue();
        carrier.Name.Should().Be("Van Dijk Transport");
        rateCard.ShipperId.Value.Should().Be("SHP-0001");
        rateCard.Versions.Should().Equal(Versions);
        carrierRateCard.CarrierId.Value.Should().Be("CAR-0001");
        vehicle.Registration.Should().Be("VN-101-A");
        vehicle.Type.Should().Be(VehicleType.Van);
        vehicle.Active.Should().BeTrue();
        driver.Name.Should().Be("Anouk de Vries");
        driver.Licences.Should().Equal(LicenceCategory.B, LicenceCategory.CE);
        await _shippers.DidNotReceive().AddAsync(Arg.Any<Shipper>(), Arg.Any<CancellationToken>());
        await _carriers.DidNotReceive().AddAsync(Arg.Any<Carrier>(), Arg.Any<CancellationToken>());
        await _rateCards.DidNotReceive().AddAsync(Arg.Any<RateCard>(), Arg.Any<CancellationToken>());
        await _carrierRateCards.DidNotReceive().AddAsync(Arg.Any<CarrierRateCard>(), Arg.Any<CancellationToken>());
        await _shippers.Received().UpdateAsync(shipper, Arg.Any<CancellationToken>());
        await _carriers.Received().UpdateAsync(carrier, Arg.Any<CancellationToken>());
        await _rateCards.Received().UpdateAsync(rateCard, Arg.Any<CancellationToken>());
        await _carrierRateCards.Received().UpdateAsync(carrierRateCard, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_WithNothingInTheSeed_LoadsNothing()
    {
        var result = await Load(new MasterDataSeed([], [], [], [], [], []));

        result.Should().Be(new LoadMasterDataResult(0, 0));
    }

    [Fact]
    public async Task Load_WithoutACommand_Throws()
    {
        var handling = async () => await new LoadMasterDataHandler(_shippers, _carriers, _rateCards, _carrierRateCards, _vehicles, _drivers).Handle(null!, CancellationToken.None);

        await handling.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<LoadMasterDataResult> Load(MasterDataSeed seed) =>
        await new LoadMasterDataHandler(_shippers, _carriers, _rateCards, _carrierRateCards, _vehicles, _drivers)
            .Handle(new LoadMasterDataCommand(seed), CancellationToken.None);
}

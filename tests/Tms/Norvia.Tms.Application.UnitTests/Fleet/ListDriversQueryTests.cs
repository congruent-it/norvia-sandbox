using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.ListDrivers;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet;

/// <summary>The licence-cover rule as the TMS query side shows it: each Driver with the Vehicle types their licences cover.</summary>
public sealed class ListDriversQueryTests
{
    private readonly IDriverDirectory _drivers = Substitute.For<IDriverDirectory>();

    [Fact]
    public async Task Handle_ActiveDrivers_ListsEachWithTheVehicleTypesTheirLicencesCover()
    {
        _drivers.ListActive().Returns(
        [
            Driver("DRV-0001", "Anouk de Vries", LicenceCategory.CE, LicenceCategory.B),
            Driver("DRV-0004", "Bram Visser", LicenceCategory.C),
            Driver("DRV-0006", "Cor Smit", LicenceCategory.B),
        ]);

        var drivers = await new ListDriversHandler(_drivers).Handle(new ListDriversQuery(), CancellationToken.None);

        drivers.Should().BeEquivalentTo(
            [
                new DriverView("DRV-0001", "Anouk de Vries", [LicenceCategory.B, LicenceCategory.CE], [VehicleType.Van, VehicleType.RigidTruck, VehicleType.TractorWithTrailer]),
                new DriverView("DRV-0004", "Bram Visser", [LicenceCategory.C], [VehicleType.Van, VehicleType.RigidTruck]),
                new DriverView("DRV-0006", "Cor Smit", [LicenceCategory.B], [VehicleType.Van]),
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_NoActiveDrivers_ListsNothing()
    {
        _drivers.ListActive().Returns([]);

        var drivers = await new ListDriversHandler(_drivers).Handle(new ListDriversQuery(), CancellationToken.None);

        drivers.Should().BeEmpty();
    }

    private static Driver Driver(string id, string name, params LicenceCategory[] licences) =>
        new(new DriverId(id), name, licences.ToHashSet(), Active: true);
}

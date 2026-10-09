using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.ListVehicles;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet;

public sealed class ListVehiclesQueryTests
{
    private readonly IVehicleDirectory _vehicles = Substitute.For<IVehicleDirectory>();

    [Fact]
    public async Task Handle_ActiveVehicles_ListsEachInTheDirectorysOrderWithTheLicenceItNeeds()
    {
        _vehicles.ListActive().Returns(
        [
            new Vehicle(new VehicleId("VEH-0001"), "VB-101-A", VehicleType.Van, 1200, 4, true),
            new Vehicle(new VehicleId("VEH-0003"), "VB-103-C", VehicleType.RigidTruck, 10000, 18, true),
            new Vehicle(new VehicleId("VEH-0006"), "VB-106-F", VehicleType.TractorWithTrailer, 24000, 33, true),
        ]);

        var vehicles = await new ListVehiclesHandler(_vehicles).Handle(new ListVehiclesQuery(), CancellationToken.None);

        vehicles.Should().Equal(
            new VehicleView("VEH-0001", "VB-101-A", VehicleType.Van, 1200, 4, LicenceCategory.B),
            new VehicleView("VEH-0003", "VB-103-C", VehicleType.RigidTruck, 10000, 18, LicenceCategory.C),
            new VehicleView("VEH-0006", "VB-106-F", VehicleType.TractorWithTrailer, 24000, 33, LicenceCategory.CE));
    }

    [Fact]
    public async Task Handle_NoActiveVehicles_ListsNothing()
    {
        _vehicles.ListActive().Returns([]);

        var vehicles = await new ListVehiclesHandler(_vehicles).Handle(new ListVehiclesQuery(), CancellationToken.None);

        vehicles.Should().BeEmpty();
    }
}

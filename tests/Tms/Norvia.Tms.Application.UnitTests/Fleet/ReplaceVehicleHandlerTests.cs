using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.ReplaceVehicle;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet;

public sealed class ReplaceVehicleHandlerTests
{
    private static readonly VehicleId Id = new("VEH-0001");
    private readonly IVehicleDirectory _directory = Substitute.For<IVehicleDirectory>();
    private readonly IVehicleReplicas _replicas = Substitute.For<IVehicleReplicas>();

    private ReplaceVehicleHandler Handler => new(_directory, _replicas, NullLogger<ReplaceVehicleHandler>.Instance);

    [Fact]
    public async Task Handle_ANewVehicle_IsKeptWholeAtItsRevision()
    {
        var result = await Handler.Handle(new ReplaceVehicleCommand("VEH-0100", "VN-9", "RigidTruck", 9000, 14, true, 1), CancellationToken.None);

        result.Value.Should().Be(new ReplaceVehicleResult(Applied: true, 1));
        await _replicas.Received().ReplaceAsync(new Vehicle(new VehicleId("VEH-0100"), "VN-9", VehicleType.RigidTruck, 9000, 14, true, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AHigherRevisionThatMakesTheVehicleInactive_ReplacesTheCopyWithOnlyThatChanged()
    {
        _directory.Find(Id).Returns(new Vehicle(Id, "VN-101-A", VehicleType.Van, 1200, 4, true));

        var result = await Handler.Handle(new ReplaceVehicleCommand("VEH-0001", "VN-101-A", "Van", 1200, 4, false, 2), CancellationToken.None);

        result.Value.Should().Be(new ReplaceVehicleResult(Applied: true, 2));
        await _replicas.Received().ReplaceAsync(new Vehicle(Id, "VN-101-A", VehicleType.Van, 1200, 4, false, 2), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Handle_ARevisionThatIsNotHigher_ChangesNothingAndStillSucceeds(int revision)
    {
        _directory.Find(Id).Returns(new Vehicle(Id, "VN-101-A", VehicleType.Van, 1200, 4, true, 2));

        var result = await Handler.Handle(new ReplaceVehicleCommand("VEH-0001", "Late", "Van", 1200, 4, false, revision), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReplaceVehicleResult(Applied: false, 2));
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Vehicle>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "VN-1", "Van", 1200, 4, 1)]
    [InlineData("VEH-1", " ", "Van", 1200, 4, 1)]
    [InlineData("VEH-1", "VN-1", "Boat", 1200, 4, 1)]
    [InlineData("VEH-1", "VN-1", "van", 1200, 4, 1)]
    [InlineData("VEH-1", "VN-1", "5", 1200, 4, 1)]
    [InlineData("VEH-1", "VN-1", "Van", 0, 4, 1)]
    [InlineData("VEH-1", "VN-1", "Van", 1200, -1, 1)]
    [InlineData("VEH-1", "VN-1", "Van", 1200, 4, 0)]
    public async Task Handle_AnUnreadableRecord_IsRefusedWithMasterDataUnreadable(string id, string registration, string type, int payload, int pallets, int revision)
    {
        var result = await Handler.Handle(new ReplaceVehicleCommand(id, registration, type, payload, pallets, true, revision), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataUnreadable");
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Vehicle>(), Arg.Any<CancellationToken>());
    }
}

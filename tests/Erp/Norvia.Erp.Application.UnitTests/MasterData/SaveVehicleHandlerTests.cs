using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.Vehicles;
using Norvia.Erp.Application.MasterData.Vehicles.Save;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Application.UnitTests.MasterData;

public sealed class SaveVehicleHandlerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly IRepository<Vehicle> _vehicles = Substitute.For<IRepository<Vehicle>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private SaveVehicleHandler Handler => new(_vehicles, _outbox, _time, NullLogger<SaveVehicleHandler>.Instance);

    private void Have(Vehicle vehicle) =>
        _vehicles.FirstOrDefaultAsync(Arg.Any<ISpecification<Vehicle>>(), Arg.Any<CancellationToken>()).Returns(vehicle);

    private static Vehicle Van() => new(new VehicleId("VEH-0001"), "VN-101-A", VehicleType.Van, 1200, 4, true);

    [Fact]
    public async Task Handle_ForANewVehicle_AddsItAtRevisionOneAndPublishesTheWholeRecord()
    {
        var result = await Handler.Handle(new SaveVehicleCommand("VEH-0100", "VN-900-Z", "RigidTruck", 9000, 14, true), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Created);
        result.Value.Vehicle.Should().Be(new VehicleView("VEH-0100", "VN-900-Z", "RigidTruck", 9000, 14, true, 1));
        await _vehicles.Received().AddAsync(Arg.Is<Vehicle>(vehicle => vehicle.Id.Value == "VEH-0100" && vehicle.FinanceOwned), Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e =>
            e is VehicleV1 && e.EventType == "Vehicle" && e.Topic == Topics.MasterDataEvents && ((VehicleV1)e).Vehicle.Revision == 1));
    }

    [Fact]
    public async Task Handle_WithARegistrationAnotherVehicleHas_RefusesAndPublishesNothing()
    {
        Have(Van());

        var result = await Handler.Handle(new SaveVehicleCommand("VEH-0100", "VN-101-A", "Van", 1200, 4, true), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("VehicleRegistrationTaken");
        await _vehicles.DidNotReceive().AddAsync(Arg.Any<Vehicle>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_MakingAVehicleInactive_StoresOnlyTheFlagAndPublishesTheRecord()
    {
        var vehicle = Van();
        Have(vehicle);

        var result = await Handler.Handle(new SaveVehicleCommand("VEH-0001", "VN-101-A", "Van", 1200, 4, false), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Updated);
        result.Value.Vehicle.Should().Be(new VehicleView("VEH-0001", "VN-101-A", "Van", 1200, 4, false, 2));
        await _vehicles.Received().UpdateAsync(vehicle, Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e => !((VehicleV1)e).Vehicle.Active && ((VehicleV1)e).Vehicle.Revision == 2));
    }

    [Fact]
    public async Task Handle_ForTheSameContent_ChangesAndPublishesNothing()
    {
        Have(Van());

        var result = await Handler.Handle(new SaveVehicleCommand("VEH-0001", "VN-101-A", "Van", 1200, 4, true), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Unchanged);
        result.Value.Vehicle.Revision.Should().Be(1);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _vehicles.DidNotReceive().UpdateAsync(Arg.Any<Vehicle>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "VN-1")]
    [InlineData("VEH-1", " ")]
    public async Task Handle_WithoutAnIdOrARegistration_IsRefusedAsRequired(string id, string registration)
    {
        var result = await Handler.Handle(new SaveVehicleCommand(id, registration, "Van", 1200, 4, true), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldRequired");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithATooLongRegistration_IsRefusedAsTooLong()
    {
        var result = await Handler.Handle(new SaveVehicleCommand("VEH-1", new string('x', 51), "Van", 1200, 4, true), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldTooLong");
    }

    [Theory]
    [InlineData("Bicycle", 1200, 4)]
    [InlineData("van", 1200, 4)]
    [InlineData("7", 1200, 4)]
    [InlineData("Van", 0, 4)]
    [InlineData("Van", 1200, -1)]
    public async Task Handle_WithAnUnknownTypeOrImpossibleCapacity_IsRefusedAsInvalid(string type, int payload, int pallets)
    {
        var result = await Handler.Handle(new SaveVehicleCommand("VEH-1", "VN-1", type, payload, pallets, true), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldInvalid");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }
}

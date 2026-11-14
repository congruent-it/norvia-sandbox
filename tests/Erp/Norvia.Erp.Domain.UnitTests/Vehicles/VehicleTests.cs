using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Domain.UnitTests.Vehicles;

public sealed class VehicleTests
{
    private static readonly VehicleId Id = new("VEH-0001");

    private static Vehicle Van() => new(Id, "VN-101-A", VehicleType.Van, 1200, 4, active: true);

    [Fact]
    public void Create_KeepsWhatItIsGiven_AtRevisionOne()
    {
        var vehicle = Van();

        vehicle.Id.Should().Be(Id);
        vehicle.Registration.Should().Be("VN-101-A");
        vehicle.Type.Should().Be(VehicleType.Van);
        vehicle.PayloadKg.Should().Be(1200);
        vehicle.PalletPlaces.Should().Be(4);
        vehicle.Active.Should().BeTrue();
        vehicle.Revision.Should().Be(1);
        vehicle.FinanceOwned.Should().BeFalse();
    }

    [Fact]
    public void Create_WithoutARegistration_Throws() =>
        FluentActions.Invoking(() => new Vehicle(Id, " ", VehicleType.Van, 1200, 4, true)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1200, -1)]
    public void Create_WithoutAPositivePayloadOrWithNegativePallets_Throws(int payload, int pallets) =>
        FluentActions.Invoking(() => new Vehicle(Id, "VN-1", VehicleType.Van, payload, pallets, true)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Edit_ReplacesTheFieldsRaisesTheRevisionAndTakesTheRecordFromTheSeedFile()
    {
        var vehicle = Van();

        var changed = vehicle.Edit("VN-999-Z", VehicleType.RigidTruck, 9000, 14, active: false);

        changed.Should().BeTrue();
        vehicle.Registration.Should().Be("VN-999-Z");
        vehicle.Type.Should().Be(VehicleType.RigidTruck);
        vehicle.PayloadKg.Should().Be(9000);
        vehicle.PalletPlaces.Should().Be(14);
        vehicle.Active.Should().BeFalse();
        vehicle.Revision.Should().Be(2);
        vehicle.FinanceOwned.Should().BeTrue();
    }

    [Fact]
    public void Edit_MakingItInactive_ChangesOnlyTheActiveFlag()
    {
        var vehicle = Van();

        vehicle.Edit("VN-101-A", VehicleType.Van, 1200, 4, active: false).Should().BeTrue();

        vehicle.Registration.Should().Be("VN-101-A");
        vehicle.PayloadKg.Should().Be(1200);
        vehicle.Active.Should().BeFalse();
    }

    [Fact]
    public void Edit_WithTheContentItAlreadyHas_SaysNothingChangedAndKeepsTheRevisionAndTheSeedFileInCharge()
    {
        var vehicle = Van();

        vehicle.Edit("VN-101-A", VehicleType.Van, 1200, 4, active: true).Should().BeFalse();

        vehicle.Revision.Should().Be(1);
        vehicle.FinanceOwned.Should().BeFalse();
    }

    [Fact]
    public void Edit_WithoutARegistration_ThrowsAndChangesNothing()
    {
        var vehicle = Van();

        FluentActions.Invoking(() => vehicle.Edit("", VehicleType.Van, 1200, 4, false)).Should().Throw<ArgumentException>();

        vehicle.Revision.Should().Be(1);
        vehicle.Active.Should().BeTrue();
    }

    [Fact]
    public void Reload_BeforeAnyEdit_TakesOverTheSeedFileAndKeepsRevisionOne()
    {
        var vehicle = Van();

        vehicle.Reload("VN-555-X", VehicleType.RigidTruck, 8000, 12, active: false);

        vehicle.Registration.Should().Be("VN-555-X");
        vehicle.Active.Should().BeFalse();
        vehicle.Revision.Should().Be(1);
    }

    [Fact]
    public void Reload_AfterAnEdit_LeavesTheEditAlone()
    {
        var vehicle = Van();
        vehicle.Edit("VN-999-Z", VehicleType.Van, 1200, 4, true);

        vehicle.Reload("VN-555-X", VehicleType.Van, 1200, 4, true);

        vehicle.Registration.Should().Be("VN-999-Z");
        vehicle.Revision.Should().Be(2);
    }

    [Fact]
    public void Reload_OfAVehicleFinanceCreated_LeavesItAlone()
    {
        var vehicle = Vehicle.CreateByFinance(Id, "VN-NEW", VehicleType.Van, 1000, 3, true);

        vehicle.Reload("VN-555-X", VehicleType.Van, 1200, 4, false);

        vehicle.Registration.Should().Be("VN-NEW");
        vehicle.FinanceOwned.Should().BeTrue();
    }

    [Fact]
    public void Reload_WithoutARegistration_Throws() =>
        FluentActions.Invoking(() => Van().Reload(" ", VehicleType.Van, 1200, 4, true)).Should().Throw<ArgumentException>();

    [Fact]
    public void VehicleId_WithoutAValue_Throws() => FluentActions.Invoking(() => new VehicleId("")).Should().Throw<ArgumentException>();

    [Fact]
    public void VehicleId_ToString_IsTheValue() => Id.ToString().Should().Be("VEH-0001");

    [Fact]
    public void VehicleByIdSpec_FindsOnlyThatVehicle()
    {
        var vehicles = new[] { new Vehicle(new VehicleId("V2"), "ZZ", VehicleType.Van, 1, 0, true), Van() };

        new VehicleByIdSpec(Id).Evaluate(vehicles).Should().ContainSingle().Which.Registration.Should().Be("VN-101-A");
    }
}

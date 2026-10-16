using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Trips;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.UnitTests.Trips;

public sealed class TripBuilderTests
{
    private static readonly DateOnly Day = new(2026, 10, 19);

    private static readonly SimulatedVehicle Van1 = new("VEH-0001", "VN-101-A", VehicleKind.Van, 1200, 4);
    private static readonly SimulatedVehicle Van2 = new("VEH-0002", "VN-102-B", VehicleKind.Van, 1200, 4);
    private static readonly SimulatedVehicle Rigid = new("VEH-0003", "RT-201-C", VehicleKind.RigidTruck, 10000, 18);
    private static readonly SimulatedVehicle Tractor = new("VEH-0006", "TT-301-F", VehicleKind.TractorWithTrailer, 24000, 33);
    private static readonly IReadOnlyList<SimulatedVehicle> Fleet = [Tractor, Rigid, Van2, Van1];

    private static readonly SimulatedDriver Ce = new("DRV-0001", "Anouk", [LicenceCategory.B, LicenceCategory.C, LicenceCategory.CE]);
    private static readonly SimulatedDriver C = new("DRV-0003", "Bram", [LicenceCategory.B, LicenceCategory.C]);
    private static readonly SimulatedDriver B1 = new("DRV-0006", "Sanne", [LicenceCategory.B]);
    private static readonly SimulatedDriver B2 = new("DRV-0007", "Tim", [LicenceCategory.B]);
    private static readonly IReadOnlyList<SimulatedDriver> Drivers = [Ce, C, B1, B2];

    private static readonly IReadOnlyList<DriverShift> Everyone =
        [new("DRV-0001", new(6, 0), new(14, 30)), new("DRV-0003", new(8, 0), new(16, 30)), new("DRV-0006", new(7, 30), new(16, 0)), new("DRV-0007", new(8, 30), new(17, 0))];

    private static TripShipment Shipment(string number, decimal kg, int pallets, int hour = 8) => new(number, new FleetLoad(kg, pallets), new DateTime(2026, 10, 19, hour, 0, 0));

    private static TripBuild Build(IEnumerable<TripShipment> shipments, IReadOnlyList<DriverShift>? shifts = null, IReadOnlyList<SimulatedDriver>? drivers = null) =>
        TripBuilder.Build(Day, shipments, Fleet, drivers ?? Drivers, shifts ?? Everyone);

    [Fact]
    public void ASmallShipment_GoesOnTheSmallestVehicle_WithTheDriverWhoseLicenceIsWorthTheLeast()
    {
        var build = Build([Shipment("SH-1", 400, 1)]);

        var draft = build.Drafts.Should().ContainSingle().Subject;
        draft.Vehicle.Should().Be(Van1);
        draft.Driver.Should().Be(B1);
        draft.Day.Should().Be(Day);
        build.Unplaced.Should().BeEmpty();
    }

    [Fact]
    public void ShipmentsThatFitTogether_ShareATrip_InTheOrderOfTheirCollection()
    {
        var build = Build([Shipment("SH-2", 300, 1, hour: 10), Shipment("SH-1", 300, 1, hour: 8)]);

        build.Drafts.Should().ContainSingle().Which.Shipments.Select(shipment => shipment.ShipmentNumber).Should().Equal("SH-1", "SH-2");
    }

    [Fact]
    public void WhenAShipmentWouldExceedThePayload_ItGetsATripOfItsOwn_WithAnotherVehicleAndDriver()
    {
        var build = Build([Shipment("SH-1", 800, 1), Shipment("SH-2", 800, 1)]);

        build.Drafts.Select(draft => draft.Vehicle).Should().Equal(Van1, Van2);
        build.Drafts.Select(draft => draft.Driver).Should().Equal(B1, B2);
    }

    [Fact]
    public void WhenShipmentsWouldExceedThePalletPlaces_TheyAreSplitOverTrips()
    {
        var build = Build([Shipment("SH-1", 100, 3), Shipment("SH-2", 100, 3)]);

        build.Drafts.Should().HaveCount(2);
    }

    [Fact]
    public void AHeavyShipment_NeedsATruck_AndADriverWhoseLicenceCoversIt()
    {
        var build = Build([Shipment("SH-1", 6800, 15)]);

        var draft = build.Drafts.Should().ContainSingle().Subject;
        draft.Vehicle.Should().Be(Rigid);
        draft.Driver.Should().Be(C);
    }

    [Fact]
    public void AShipmentOnlyATractorCarries_NeedsTheDriverWithTheCELicence()
    {
        var build = Build([Shipment("SH-1", 20000, 30)]);

        var draft = build.Drafts.Should().ContainSingle().Subject;
        draft.Vehicle.Should().Be(Tractor);
        draft.Driver.Should().Be(Ce);
    }

    [Fact]
    public void ADriverWithoutAShiftThatDay_DoesNotDrive()
    {
        var build = Build([Shipment("SH-1", 6800, 15)], shifts: [new("DRV-0001", new(6, 0), new(14, 30))]);

        // The CE Driver covers the Rigid truck's licence too.
        build.Drafts.Should().ContainSingle().Which.Driver.Should().Be(Ce);
    }

    [Fact]
    public void WithNoDriverWhoCanDriveTheVehicle_TheShipmentIsUnplaced()
    {
        var shipment = Shipment("SH-1", 6800, 15);

        var build = Build([shipment], shifts: [new("DRV-0006", new(7, 30), new(16, 0))]);

        build.Drafts.Should().BeEmpty();
        build.Unplaced.Should().Equal(shipment);
    }

    [Fact]
    public void WhenEveryVehicleHasATrip_TheNextShipmentIsUnplaced()
    {
        var shipments = new[] { Shipment("SH-1", 20000, 30), Shipment("SH-2", 20000, 30) };

        var build = Build(shipments);

        build.Drafts.Should().ContainSingle();
        build.Unplaced.Should().ContainSingle().Which.ShipmentNumber.Should().Be("SH-2");
    }

    [Fact]
    public void AShipmentNoVehicleCarries_IsUnplaced()
    {
        Build([Shipment("SH-1", 50000, 1)]).Unplaced.Should().ContainSingle();
    }

    [Fact]
    public void ThePlannedStart_IsTheStartOfTheDriversShift()
    {
        Build([Shipment("SH-1", 400, 1)]).Drafts.Single().PlannedStart.Should().Be(new TimeOnly(7, 30));
    }

    [Theory]
    [InlineData(1, 105)]
    [InlineData(3, 195)]
    [InlineData(20, 480)]
    public void ThePlannedDrivingDuration_GrowsWithTheShipments_AndStaysInsideTheDailyBudget(int shipments, int minutes)
    {
        TripBuilder.DurationOf(shipments).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public void TheSameShipmentsInAnotherOrder_GiveTheSameTrips()
    {
        var shipments = new[] { Shipment("SH-1", 800, 1, 8), Shipment("SH-2", 800, 1, 9), Shipment("SH-3", 300, 1, 10) };

        var forward = Build(shipments);
        var backward = Build(shipments.Reverse());

        backward.Drafts.Select(draft => (draft.Vehicle.Id, draft.Driver.Id, string.Join(',', draft.Shipments.Select(s => s.ShipmentNumber))))
            .Should().Equal(forward.Drafts.Select(draft => (draft.Vehicle.Id, draft.Driver.Id, string.Join(',', draft.Shipments.Select(s => s.ShipmentNumber)))));
    }
}

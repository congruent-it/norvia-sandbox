using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.UnitTests.Trips;

/// <summary>The Driver app holds the latest version of a Trip only (ADR 0015).</summary>
public sealed class DriverTripTests
{
    private static TripDetails Details(string driverName = "Joost Bakker") =>
        new(new DateOnly(2026, 10, 13), new TimeOnly(9, 0), TimeSpan.FromHours(4), new TripVehicle("VEH-0002", "NV-123-A", "RigidTruck"), driverName, "corr", []);

    [Fact]
    public void ReplaceWith_AHigherVersion_ReplacesTheHeldOne()
    {
        var trip = new DriverTrip(new TripNumber("TR-0000012"), 1, new DriverId("DRV-0002"), Details());

        trip.ReplaceWith(2, new DriverId("DRV-0001"), Details("Anouk de Vries")).Should().BeTrue();

        trip.Version.Should().Be(2);
        trip.DriverId.Should().Be(new DriverId("DRV-0001"));
        trip.Details.DriverName.Should().Be("Anouk de Vries");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void ReplaceWith_AnEqualOrLowerVersion_ChangesNothing(int version)
    {
        var trip = new DriverTrip(new TripNumber("TR-0000012"), 2, new DriverId("DRV-0002"), Details());

        trip.ReplaceWith(version, new DriverId("DRV-0001"), Details("Anouk de Vries")).Should().BeFalse();

        trip.Version.Should().Be(2);
        trip.DriverId.Should().Be(new DriverId("DRV-0002"));
    }

    [Fact]
    public void ANewTrip_WithoutAVersionOrADriver_IsABug()
    {
        Action noVersion = () => _ = new DriverTrip(new TripNumber("TR-0000012"), 0, new DriverId("DRV-0002"), Details());
        Action noDriver = () => _ = new DriverTrip(new TripNumber("TR-0000012"), 1, new DriverId(" "), Details());

        noVersion.Should().Throw<ArgumentOutOfRangeException>();
        noDriver.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TripsOfDriver_ListsTheDriversTripsOnly_InTheOrderOfTheirNumbers()
    {
        DriverTrip[] trips =
        [
            new(new TripNumber("TR-0000003"), 1, new DriverId("DRV-0002"), Details()),
            new(new TripNumber("TR-0000001"), 4, new DriverId("DRV-0002"), Details()),
            new(new TripNumber("TR-0000002"), 1, new DriverId("DRV-0001"), Details()),
        ];

        var rows = new TripsOfDriverSpecification(new DriverId("DRV-0002")).Evaluate(trips).ToList();

        rows.Select(row => row.TripNumber.Value).Should().Equal("TR-0000001", "TR-0000003");
        rows[0].Version.Should().Be(4);
    }

    [Fact]
    public void TripNumbers_AreOrderedByTheirNumber()
    {
        var one = new TripNumber("TR-0000001");
        var two = new TripNumber("TR-0000002");

        (one < two).Should().BeTrue();
        (one <= two).Should().BeTrue();
        (two > one).Should().BeTrue();
        (two >= one).Should().BeTrue();
        ((IComparable)one).CompareTo(two).Should().BeNegative();
        one.ToString().Should().Be("TR-0000001");
        ((Func<int>)(() => ((IComparable)one).CompareTo("TR-0000002"))).Should().Throw<ArgumentException>();
        new DriverId("DRV-0001").ToString().Should().Be("DRV-0001");
    }
}

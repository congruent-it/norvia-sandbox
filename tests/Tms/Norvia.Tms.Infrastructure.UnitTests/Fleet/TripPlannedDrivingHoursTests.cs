using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Infrastructure.Fleet;
using NSubstitute;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

/// <summary>The planned driving P10's Driving-hour budget reads from the Trips (P11): each Trip with a Driver and a duration.</summary>
public sealed class TripPlannedDrivingHoursTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);

    [Fact]
    public async Task ListAsync_ReportsTheDrivingOfEveryTripThatHasADriverAndADuration()
    {
        var driver = new DriverId("DRV-0001");
        var planned = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, driver, new IdempotencyKey("a"));
        planned.Plan(new TimeOnly(7, 0), TimeSpan.FromHours(6));
        var trips = Substitute.For<IReadRepository<Trip>>();
        trips.ListAsync(Arg.Any<TripsPlannedBetweenSpec>(), Arg.Any<CancellationToken>()).Returns([planned]);

        var driving = await new TripPlannedDrivingHours(trips).ListAsync(Day, Day.AddDays(1), CancellationToken.None);

        driving.Should().Equal(new PlannedDriving(driver, Day, TimeSpan.FromHours(6)));
    }

    [Fact]
    public async Task ListAsync_LeavesOutATripWithoutADriverOrADuration()
    {
        var withoutDuration = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, new DriverId("DRV-0001"), new IdempotencyKey("b"));
        var withoutDriver = Trip.CreateDraft(TripNumber.FromSequence(3), Day, null, null, new IdempotencyKey("c"));
        withoutDriver.Plan(null, TimeSpan.FromHours(2));
        var trips = Substitute.For<IReadRepository<Trip>>();
        trips.ListAsync(Arg.Any<TripsPlannedBetweenSpec>(), Arg.Any<CancellationToken>()).Returns([withoutDuration, withoutDriver]);

        (await new TripPlannedDrivingHours(trips).ListAsync(Day, Day, CancellationToken.None)).Should().BeEmpty();
    }
}

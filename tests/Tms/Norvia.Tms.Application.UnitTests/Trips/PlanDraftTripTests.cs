using Ardalis.Result;
using Norvia.Tms.Application.Fleet.ListDrivers;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.Discard;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>
/// The planned start and driving duration of a Draft (P11). Anouk's Shift is 06:00-14:00 and Joost's 08:00-16:00 on weekdays;
/// the default limits are 9 h a day (10 h at most twice a week), 56 h a week and 90 h a fortnight.
/// </summary>
public sealed class PlanDraftTripTests
{
    private static readonly TimeSpan SixHours = TimeSpan.FromHours(6);

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_AStartInsideTheShiftAndADurationThatFits_AreSaved()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        var result = await Change(trip, Van, Anouk, new TimeOnly(7, 30), SixHours);

        result.IsSuccess.Should().BeTrue();
        trip.PlannedStart.Should().Be(new TimeOnly(7, 30));
        trip.PlannedDrivingDuration.Should().Be(SixHours);
    }

    [Theory]
    [InlineData(5, 59)]
    [InlineData(14, 0)]
    [InlineData(20, 0)]
    public async Task Handle_AStartOutsideTheShift_IsRefusedWithOutsideShift_AndTheTripIsUnchanged(int hour, int minute)
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        var result = await Change(trip, Van, Anouk, new TimeOnly(hour, minute), SixHours);

        Codes(result).Should().Equal(nameof(TripRefusalCode.OutsideShift));
        trip.PlannedStart.Should().BeNull();
        trip.PlannedDrivingDuration.Should().BeNull();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheShiftStart_IsInsideTheShift()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        (await Change(trip, Van, Anouk, new TimeOnly(6, 0), SixHours)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ChangingTheDriver_ChecksTheStartAgainstTheNewDriversShift()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        var result = await Change(trip, Van, Joost, new TimeOnly(7, 0), SixHours);

        Codes(result).Should().Equal(nameof(TripRefusalCode.OutsideShift));
    }

    [Fact]
    public async Task Handle_AStartWithoutADriver_IsAccepted_ForThereIsNoShiftToCheckItAgainst()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, null, null);

        (await Change(trip, null, null, new TimeOnly(3, 0), null)).IsSuccess.Should().BeTrue();
        trip.PlannedStart.Should().Be(new TimeOnly(3, 0));
    }

    [Fact]
    public async Task Handle_ADurationOverTheDailyLimit_IsRefusedWithDrivingHoursExceeded()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        var result = await Change(trip, Van, Anouk, null, TimeSpan.FromHours(11));

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.DrivingHoursExceeded));
        trip.PlannedDrivingDuration.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ADurationOfTenHours_FitsWhileAnExtensionIsLeft()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        (await Change(trip, Van, Anouk, null, TimeSpan.FromHours(10))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ADurationThatBreaksTheWeeklyLimit_IsRefused()
    {
        // 9 h on Monday to Thursday and on Saturday and Sunday is 54 h of the week's 56, so another 9 h on Friday is too much.
        foreach (var offset in new[] { 0, 1, 2, 3, 5, 6 })
        {
            DrivenBy(Anouk, Monday.AddDays(offset), TimeSpan.FromHours(9));
        }

        var friday = _fixture.Existing(Monday.AddDays(4), Van, Anouk);

        Codes(await Change(friday, Van, Anouk, null, TimeSpan.FromHours(9))).Should().Equal(nameof(TripAvailabilityCode.DrivingHoursExceeded));
        (await Change(friday, Van, Anouk, null, TimeSpan.FromHours(2))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReplacingADuration_DoesNotCountTheOldOneAgainstTheBudget()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);
        trip.Plan(null, TimeSpan.FromHours(9));

        (await Change(trip, Van, Anouk, null, TimeSpan.FromHours(9))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithAStartAndDuration_SavesThemOnTheDraft_AndRefusesThemUnderTheSameRules()
    {
        var created = await new CreateDraftTripHandler(_fixture.Trips, _fixture.Numbers, _fixture.Checks)
            .Handle(new CreateDraftTripCommand(TripsFixture.Tuesday, Van, Anouk, new IdempotencyKey("a"), new TimeOnly(7, 0), SixHours), CancellationToken.None);
        var refused = await new CreateDraftTripHandler(_fixture.Trips, _fixture.Numbers, _fixture.Checks)
            .Handle(new CreateDraftTripCommand(TripsFixture.Tuesday.AddDays(1), Van, Anouk, new IdempotencyKey("b"), new TimeOnly(5, 0), TimeSpan.FromHours(11)), CancellationToken.None);

        created.IsSuccess.Should().BeTrue();
        _fixture.Stored.Single().PlannedStart.Should().Be(new TimeOnly(7, 0));
        _fixture.Stored.Single().PlannedDrivingDuration.Should().Be(SixHours);
        Codes(refused).Should().BeEquivalentTo(nameof(TripRefusalCode.OutsideShift), nameof(TripAvailabilityCode.DrivingHoursExceeded));
        _fixture.Stored.Should().ContainSingle();
    }

    [Fact]
    public async Task ADraftsDuration_UsesUpTheDriversHours_AndDiscardingTheDraftGivesThemBack()
    {
        foreach (var offset in new[] { 0, 2, 3, 5, 6 })
        {
            DrivenBy(Anouk, Monday.AddDays(offset), TimeSpan.FromHours(9));
        }

        var tuesday = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);
        await Change(tuesday, Van, Anouk, null, TimeSpan.FromHours(9));
        var friday = _fixture.Existing(Monday.AddDays(4), Van, Anouk);

        Codes(await Change(friday, Van, Anouk, null, TimeSpan.FromHours(9))).Should().Equal(nameof(TripAvailabilityCode.DrivingHoursExceeded));

        (await new DiscardDraftTripHandler(_fixture.Trips).Handle(new DiscardDraftTripCommand(tuesday.Number.Value), CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await Change(friday, Van, Anouk, null, TimeSpan.FromHours(9))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TheDriversTab_ShowsTheReducedBudget_AndTheHoursComeBackWhenTheDraftIsDiscarded()
    {
        // The Drivers tab reads the budget through the planned driving of the stored Trips, as in production.
        var tab = new ListDriversHandler(_fixture.Planning.Drivers, new TripPlannedDrivingHours(_fixture.Trips.AsRead()), DrivingHourLimits.Default, _fixture.Planning.Horizon, _fixture.Planning.Time);
        var draft = _fixture.Existing(Monday, Van, Anouk);
        await Change(draft, Van, Anouk, null, SixHours);

        var reduced = (await tab.Handle(new ListDriversQuery(), CancellationToken.None)).Single(driver => driver.Id == Anouk.Value).DrivingHours;
        await new DiscardDraftTripHandler(_fixture.Trips).Handle(new DiscardDraftTripCommand(draft.Number.Value), CancellationToken.None);
        var restored = (await tab.Handle(new ListDriversQuery(), CancellationToken.None)).Single(driver => driver.Id == Anouk.Value).DrivingHours;

        reduced.Should().Be(new DrivingHoursLeft(TimeSpan.FromHours(3), TimeSpan.FromHours(50), TimeSpan.FromHours(84), 2));
        restored.Should().Be(new DrivingHoursLeft(TimeSpan.FromHours(9), TimeSpan.FromHours(56), TimeSpan.FromHours(90), 2));
    }

    /// <summary>A Trip of the Driver that holds no Vehicle, so only the Driver's hours are in play.</summary>
    private void DrivenBy(DriverId driver, DateOnly day, TimeSpan duration)
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(500 + _fixture.Stored.Count), day, null, driver, new IdempotencyKey(Guid.NewGuid().ToString()));
        trip.Plan(null, duration);
        _fixture.Stored.Add(trip);
    }

    private static string[] Codes(Result result) => [.. result.ValidationErrors.Select(error => error.ErrorCode)];

    private static string[] Codes<T>(Result<T> result) => [.. result.ValidationErrors.Select(error => error.ErrorCode)];

    private async Task<Result> Change(Trip trip, VehicleId? vehicle, DriverId? driver, TimeOnly? start, TimeSpan? duration) =>
        await new ChangeDraftTripHandler(_fixture.Trips, _fixture.Checks)
            .Handle(new ChangeDraftTripCommand(trip.Number.Value, vehicle, driver, start, duration), CancellationToken.None);
}

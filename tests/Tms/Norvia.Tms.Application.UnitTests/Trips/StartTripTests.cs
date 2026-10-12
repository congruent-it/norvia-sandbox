using Ardalis.Result;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Trips.Start;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>The Driver starts a Dispatched Trip on its day (P12). Today is Monday 12 October 2026, the Trip's day is Tuesday 13 October.</summary>
public sealed class StartTripTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task ADispatchedTripOnItsDay_IsInProgress_RaisesOneTripStarted_AndSaves()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Planning.Time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));
        _fixture.Trips.ClearReceivedCalls();

        var result = await Start(trip.Number.Value, PlanningEditsFixture.Anouk, "corr-start");

        result.Value.Should().Be(new StartTripResult(trip.Number.Value, 1, "corr-start"));
        trip.Status.Should().Be(TripStatus.InProgress);
        var started = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripStartedEvent>().Subject;
        started.CorrelationId.Value.Should().Be("corr-start");
        started.DriverId.Should().Be(PlanningEditsFixture.Anouk);
        started.StartedAt.Should().Be(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheDayIsCountedInNorviasTime_NotUtc()
    {
        var trip = await _fixture.DispatchedAsync();
        // 00:30 on 13 October in Amsterdam is still 12 October in UTC.
        _fixture.Planning.Time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 0, 30, 0, TimeSpan.FromHours(2)));

        (await Start(trip.Number.Value, PlanningEditsFixture.Anouk, "corr-start")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AnotherDay_IsRefusedWithNotTripDay_AndNothingChanges()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Trips.ClearReceivedCalls();

        var result = await Start(trip.Number.Value, PlanningEditsFixture.Anouk, "corr-start");

        Codes(result).Should().Equal(nameof(TripRefusalCode.NotTripDay));
        trip.Status.Should().Be(TripStatus.Dispatched);
        trip.DomainEvents.Should().BeEmpty();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherDriver_AndAnUnknownTrip_AreRefusedWithTripNotFound()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Planning.Time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));

        Codes(await Start(trip.Number.Value, PlanningEditsFixture.Joost, "corr-start")).Should().Equal(nameof(TripRefusalCode.TripNotFound));
        Codes(await Start("TR-0009999", PlanningEditsFixture.Anouk, "corr-start")).Should().Equal(nameof(TripRefusalCode.TripNotFound));
        trip.Status.Should().Be(TripStatus.Dispatched);
    }

    [Fact]
    public async Task ADraft_AndATripAlreadyInProgress_AreRefusedWithTripNotDispatched()
    {
        var draft = _fixture.Existing(TripsFixture.Tuesday, PlanningEditsFixture.Van, PlanningEditsFixture.Anouk);
        var started = await _fixture.DispatchedAsync();
        _fixture.Planning.Time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 6, 5, 0, TimeSpan.Zero));
        (await Start(started.Number.Value, PlanningEditsFixture.Anouk, "corr-start")).IsSuccess.Should().BeTrue();
        started.ClearDomainEvents();
        _fixture.Trips.ClearReceivedCalls();

        Codes(await Start(draft.Number.Value, PlanningEditsFixture.Anouk, "corr-start")).Should().Equal(nameof(TripRefusalCode.TripNotDispatched));
        Codes(await Start(started.Number.Value, PlanningEditsFixture.Anouk, "corr-again")).Should().Equal(nameof(TripRefusalCode.TripNotDispatched));
        started.DomainEvents.Should().BeEmpty();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private async Task<Result<StartTripResult>> Start(string number, DriverId driver, string correlationId) =>
        await new StartTripHandler(_fixture.Trips, _fixture.Planning.Horizon, _fixture.Planning.Time)
            .Handle(new StartTripCommand(number, driver.Value, correlationId), CancellationToken.None);

    private static IEnumerable<string> Codes(Result<StartTripResult> result) => result.ValidationErrors.Select(error => error.ErrorCode);
}

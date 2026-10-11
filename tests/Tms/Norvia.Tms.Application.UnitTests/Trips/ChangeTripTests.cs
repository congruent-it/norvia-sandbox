using Ardalis.Result;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class ChangeTripTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_PicksAVehicleAndDriverForAnEmptyDraft()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, null, null);

        var result = await Handle(trip.Number.Value, Van, Anouk);

        result.IsSuccess.Should().BeTrue();
        trip.VehicleId.Should().Be(Van);
        trip.DriverId.Should().Be(Anouk);
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeepingTheSameVehicleAndDriver_IsNotAnotherTripOfItsOwn()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        (await Handle(trip.Number.Value, Van, Joost)).IsSuccess.Should().BeTrue();
        trip.DriverId.Should().Be(Joost);
    }

    [Fact]
    public async Task Handle_AVehicleAnotherTripHolds_IsRefused_AndTheTripIsUnchanged()
    {
        _fixture.Existing(TripsFixture.Tuesday, Van, null);
        var trip = _fixture.Existing(TripsFixture.Tuesday, null, Anouk);

        var result = await Handle(trip.Number.Value, Van, Anouk);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.VehicleNotAvailable));
        trip.VehicleId.Should().BeNull();
        trip.DriverId.Should().Be(Anouk);
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnknownTrip_IsRefusedWithTripNotFound()
    {
        var result = await Handle("TR-0009999", Van, Anouk);

        Codes(result).Should().Equal(nameof(TripRefusalCode.TripNotFound));
    }

    [Fact]
    public async Task Handle_ANumberThatIsNoTripNumber_IsRefusedWithTripNotFound()
    {
        Codes(await Handle("nonsense", Van, Anouk)).Should().Equal(nameof(TripRefusalCode.TripNotFound));
    }

    [Fact]
    public async Task Handle_ATripUnderWay_IsRefusedWithTripUnderWay()
    {
        var trip = await _fixture.DispatchedAsync();
        trip.MarkInProgress();

        Codes(await Handle(trip.Number.Value, Van, Joost)).Should().Equal(nameof(TripRefusalCode.TripUnderWay));
        trip.DriverId.Should().Be(Anouk);
    }

    [Fact]
    public async Task Handle_ChangingToNothing_GivesTheVehicleAndDriverBack()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        (await Handle(trip.Number.Value, null, null)).IsSuccess.Should().BeTrue();

        trip.VehicleId.Should().BeNull();
        trip.DriverId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_GivingTheVehicleAndDriverBack_IsAllowedOnADayThatLeftThePlanningHorizon()
    {
        var trip = _fixture.Existing(Monday.AddDays(-3), Van, Anouk);

        (await Handle(trip.Number.Value, null, null)).IsSuccess.Should().BeTrue();

        trip.VehicleId.Should().BeNull();
        trip.DriverId.Should().BeNull();
    }

    private static string[] Codes(Result result) => [.. result.ValidationErrors.Select(error => error.ErrorCode)];

    private async Task<Result> Handle(string number, VehicleId? vehicle, DriverId? driver) =>
        await new ChangeTripHandler(_fixture.Trips, _fixture.Checks, _fixture.TripShipments, _fixture.Amendments)
            .Handle(new ChangeTripCommand(number, vehicle, driver), CancellationToken.None);
}

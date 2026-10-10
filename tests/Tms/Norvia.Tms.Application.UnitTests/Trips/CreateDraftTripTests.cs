using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class CreateDraftTripTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_AnEmptyDraftForAnOpenDay_IsSaved()
    {
        var result = await Handle(TripsFixture.Tuesday, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Number.Should().Be("TR-0000001");
        var trip = _fixture.Stored.Single();
        trip.Status.Should().Be(TripStatus.Draft);
        trip.Day.Should().Be(TripsFixture.Tuesday);
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnAvailableVehicleAndDriver_AreHeldByTheDraft()
    {
        var result = await Handle(TripsFixture.Tuesday, Van, Anouk);

        result.IsSuccess.Should().BeTrue();
        _fixture.Stored.Single().VehicleId.Should().Be(Van);
        _fixture.Stored.Single().DriverId.Should().Be(Anouk);
    }

    [Fact]
    public async Task Handle_ADayOutsideTheHorizon_IsRefusedWithDayNotOpen_AndNothingIsSaved()
    {
        var result = await Handle(new DateOnly(2026, 12, 1), null, null);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.DayNotOpen));
        _fixture.Stored.Should().BeEmpty();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ASecondTripForTheSameVehicle_IsRefusedEvenWhenTheFirstIsADraft()
    {
        _fixture.Existing(TripsFixture.Tuesday, Van, null);

        var result = await Handle(TripsFixture.Tuesday, Van, Joost);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.VehicleNotAvailable));
        _fixture.Stored.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ASecondTripForTheSameDriver_IsRefused()
    {
        _fixture.Existing(TripsFixture.Tuesday, null, Anouk);

        var result = await Handle(TripsFixture.Tuesday, Van, Anouk);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.DriverNotAvailable));
    }

    [Fact]
    public async Task Handle_ATripOnAnotherDay_DoesNotBlockTheVehicleAndDriver()
    {
        _fixture.Existing(TripsFixture.Tuesday.AddDays(1), Van, Anouk);

        (await Handle(TripsFixture.Tuesday, Van, Anouk)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ADriverWithoutAShiftThatDay_IsRefusedWithDriverNotAvailable()
    {
        // Both Rosters are Monday to Friday; Saturday 17 October has no Shift.
        var result = await Handle(new DateOnly(2026, 10, 17), null, Anouk);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.DriverNotAvailable));
    }

    [Fact]
    public async Task Handle_ALicenceThatDoesNotCoverTheVehicle_IsRefusedWithLicenceNotHeld()
    {
        var result = await Handle(TripsFixture.Tuesday, Truck, Anouk);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.LicenceNotHeld));
    }

    [Fact]
    public async Task Handle_AVehicleOffTheRoad_IsRefusedWithVehicleNotAvailable()
    {
        _fixture.Planning.Day(TripsFixture.Tuesday).TakeOffTheRoad(Van);

        var result = await Handle(TripsFixture.Tuesday, Van, null);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.VehicleNotAvailable));
    }

    [Fact]
    public async Task Handle_TheSameRequestSentAgain_IsAnsweredWithTheDraftItCreated_AndCreatesNoOther()
    {
        var first = await Handle(TripsFixture.Tuesday, Van, Anouk, key: "retry-1");

        var again = await Handle(TripsFixture.Tuesday, Van, Anouk, key: "retry-1");

        again.IsSuccess.Should().BeTrue("the Vehicle and Driver it holds are its own, not another Trip's");
        again.Value.Number.Should().Be(first.Value.Number);
        _fixture.Stored.Should().ContainSingle();
    }

    private static string[] Codes(Ardalis.Result.Result<CreateDraftTripResult> result) => [.. result.ValidationErrors.Select(error => error.ErrorCode)];

    private async Task<Ardalis.Result.Result<CreateDraftTripResult>> Handle(DateOnly day, VehicleId? vehicle, DriverId? driver, string? key = null) =>
        await new CreateDraftTripHandler(_fixture.Trips, _fixture.Numbers, _fixture.Checks)
            .Handle(new CreateDraftTripCommand(day, vehicle, driver, new IdempotencyKey(key ?? Guid.NewGuid().ToString())), CancellationToken.None);
}

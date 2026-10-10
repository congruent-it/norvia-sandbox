using Ardalis.Result;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.Discard;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class DiscardDraftTripTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_ADraft_IsDeleted_AndItsVehicleAndDriverCanBeUsedOnAnotherTrip()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        var result = await Discard(trip.Number.Value);

        result.IsSuccess.Should().BeTrue();
        _fixture.Stored.Should().BeEmpty();
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        var another = await new CreateDraftTripHandler(_fixture.Trips, _fixture.Numbers, _fixture.Checks, _fixture.TripShipments)
            .Handle(new CreateDraftTripCommand(TripsFixture.Tuesday, Van, Anouk, new IdempotencyKey("second")), CancellationToken.None);
        another.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ADispatchedTrip_IsNeverDeleted()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(trip, TripStatus.Dispatched);

        var result = await Discard(trip.Number.Value);

        result.ValidationErrors.Single().ErrorCode.Should().Be(nameof(TripRefusalCode.TripNotDraft));
        _fixture.Stored.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AnUnknownTrip_IsRefusedWithTripNotFound()
    {
        var result = await Discard("TR-0009999");

        result.ValidationErrors.Single().ErrorCode.Should().Be(nameof(TripRefusalCode.TripNotFound));
    }

    private async Task<Result> Discard(string number) =>
        await new DiscardDraftTripHandler(_fixture.Trips).Handle(new DiscardDraftTripCommand(number), CancellationToken.None);
}

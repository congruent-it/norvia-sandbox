using Ardalis.Result;
using Norvia.Tms.Application.Trips.AddShipments;
using Norvia.Tms.Application.Trips.TakeShipmentOff;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>
/// Taking a Shipment off a Trip (P11): until it is Collected, its two Stops leave the Trip and it is Planned again, with one ShipmentTakenOffTrip
/// under the Planner's correlation ID. On a Dispatched Trip, also one In progress, that is an Amend too, with the next version under the same ID.
/// </summary>
public sealed class TakeShipmentOffTripTests
{
    private static readonly DateOnly Tuesday = TripsFixture.Tuesday;

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task OnADispatchedTrip_TheShipmentIsPlannedAgain_ItsStopsLeave_AndVersion2GoesOutWithoutThemUnderTheSameCorrelationId()
    {
        var trip = await _fixture.DispatchedAsync();
        var (first, second) = (_fixture.StoredShipments.Single(), _fixture.ReleasedShipment(Tuesday));
        await Add(trip, second);
        trip.ClearDomainEvents();
        _fixture.Trips.ClearReceivedCalls();

        var result = await TakeOff(trip, first, "Consignee closed", "corr-take-off");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new TakeShipmentOffTripResult(trip.Number.Value, first.Number.Value, 3, "corr-take-off"));
        first.Status.Should().Be(ShipmentStatus.Planned);
        trip.Stops.Select(stop => (stop.ShipmentId, stop.Kind, stop.Position)).Should().Equal((second.Id, StopKind.Collection, 0), (second.Id, StopKind.Delivery, 1));
        trip.Version.Should().Be(3);
        var dispatched = trip.DomainEvents.OfType<TripDispatchedEvent>().Should().ContainSingle().Subject;
        dispatched.CorrelationId.Value.Should().Be("corr-take-off");
        dispatched.Shipments.Select(item => item.Shipment.Id).Should().Equal(second.Id);
        var takenOff = trip.DomainEvents.OfType<ShipmentTakenOffTripEvent>().Should().ContainSingle().Subject;
        (takenOff.CorrelationId.Value, takenOff.Version, takenOff.Reason, takenOff.Shipment.Id).Should().Be(("corr-take-off", 3, "Consignee closed", first.Id));
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnADispatchedTrip_TheLastShipmentCanBeTakenOff_AndTheVersionGoesOutWithoutStops()
    {
        var trip = await _fixture.DispatchedAsync();
        var shipment = _fixture.StoredShipments.Single();

        (await TakeOff(trip, shipment, "Cancelled", "corr")).IsSuccess.Should().BeTrue();

        trip.Stops.Should().BeEmpty();
        trip.Version.Should().Be(2);
        trip.DomainEvents.OfType<TripDispatchedEvent>().Should().ContainSingle().Which.Shipments.Should().BeEmpty();
    }

    [Fact]
    public async Task OnADraft_TheShipmentIsPlannedAgain_AndOnlyShipmentTakenOffTripIsRaised_WithoutAVersion()
    {
        var draft = _fixture.Existing(Tuesday, Van, Anouk);
        var shipment = _fixture.ReleasedShipment(Tuesday);
        await Add(draft, shipment);

        var result = await TakeOff(draft, shipment, "Wrong day", "corr-draft");

        result.Value.Version.Should().BeNull();
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        draft.Stops.Should().BeEmpty();
        draft.Version.Should().Be(0);
        draft.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentTakenOffTripEvent>().Which.Version.Should().BeNull();
    }

    [Theory]
    [InlineData(ShipmentStatus.Collected)]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivered)]
    public async Task AShipmentThatIsCollected_IsRefusedWithShipmentCollected_AndNothingChanges(ShipmentStatus status)
    {
        var trip = await _fixture.DispatchedAsync();
        var shipment = _fixture.StoredShipments.Single();
        Move(shipment, status);

        var result = await TakeOff(trip, shipment, "Too late", "corr");

        Codes(result).Should().Equal(nameof(TripRefusalCode.ShipmentCollected));
        shipment.Status.Should().Be(status);
        trip.Stops.Should().HaveCount(2);
        trip.Version.Should().Be(1);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task OnceTheTripIsInProgress_AnUncollectedShipmentCanStillBeTakenOff_AndTheNextVersionGoesOut()
    {
        var trip = await _fixture.DispatchedAsync();
        var (first, second) = (_fixture.StoredShipments.Single(), _fixture.ReleasedShipment(Tuesday));
        await Add(trip, second);
        trip.Start(new CorrelationId("corr-start"), DateTimeOffset.UtcNow);
        trip.ClearDomainEvents();
        Move(first, ShipmentStatus.Collected);

        Codes(await TakeOff(trip, first, "Too late", "corr-1")).Should().Equal(nameof(TripRefusalCode.ShipmentCollected));
        var result = await TakeOff(trip, second, "Not today", "corr-2");

        result.IsSuccess.Should().BeTrue();
        second.Status.Should().Be(ShipmentStatus.Planned);
        trip.Status.Should().Be(TripStatus.InProgress);
        trip.Version.Should().Be(3);
        trip.DomainEvents.OfType<TripDispatchedEvent>().Should().ContainSingle().Which.Shipments.Select(item => item.Shipment.Id).Should().Equal(first.Id);
    }

    [Fact]
    public async Task AShipmentNotOnTheTrip_UnknownShipmentAndUnknownTrip_AreRefused()
    {
        var trip = await _fixture.DispatchedAsync();
        var other = _fixture.ReleasedShipment(Tuesday);

        Codes(await TakeOff(trip, other, "Why", "corr")).Should().Equal(nameof(TripRefusalCode.ShipmentNotOnTrip));
        Codes(await Handle(new TakeShipmentOffTripCommand(trip.Number.Value, "SH-9999999", "Why", "corr"))).Should().Equal(nameof(TripRefusalCode.ShipmentNotFound));
        Codes(await Handle(new TakeShipmentOffTripCommand("TR-9999999", other.Number.Value, "Why", "corr"))).Should().Equal(nameof(TripRefusalCode.TripNotFound));

        other.Status.Should().Be(ShipmentStatus.Released);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheDriverIsNoLongerActive_ADispatchedTripRefusesWithVehicleOrDriverInactive_AndNothingChanges()
    {
        var trip = await _fixture.DispatchedAsync();
        var shipment = _fixture.StoredShipments.Single();
        _fixture.Planning.Drivers.ListActive().Returns([]);

        Codes(await TakeOff(trip, shipment, "Why", "corr")).Should().Equal(nameof(TripRefusalCode.VehicleOrDriverInactive));

        shipment.Status.Should().Be(ShipmentStatus.Dispatched);
        trip.Stops.Should().HaveCount(2);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ATakenOffShipment_CanBeReleasedAgain_AndPutOnATripAgain()
    {
        var trip = await _fixture.DispatchedAsync();
        var shipment = _fixture.StoredShipments.Single();
        await TakeOff(trip, shipment, "Next week", "corr");
        var order = _fixture.StoredOrders.Single();

        shipment.Release(order, new IdempotencyKey("release-again"), new CorrelationId("corr-re"), _fixture.Planning.Time.GetUtcNow()).Should().NotBeNull();

        shipment.Status.Should().Be(ShipmentStatus.Released);
        (await Add(trip, shipment)).IsSuccess.Should().BeTrue();
        trip.Stops.Should().HaveCount(2);
    }

    private static void Move(Shipment shipment, ShipmentStatus status)
    {
        typeof(Shipment).GetProperty(nameof(Shipment.Status))!.SetValue(shipment, status);
    }

    private static string[] Codes(Result<TakeShipmentOffTripResult> result) => [.. result.ValidationErrors.Select(error => error.ErrorCode)];

    private ValueTask<Result<TakeShipmentOffTripResult>> Handle(TakeShipmentOffTripCommand command) =>
        new TakeShipmentOffTripHandler(_fixture.Trips, _fixture.Shipments, _fixture.TripShipments, _fixture.Amendments, _fixture.Planning.Time).Handle(command, CancellationToken.None);

    private async Task<Result<TakeShipmentOffTripResult>> TakeOff(Trip trip, Shipment shipment, string reason, string correlationId) =>
        await Handle(new TakeShipmentOffTripCommand(trip.Number.Value, shipment.Number.Value, reason, correlationId));

    private async Task<Result<AddShipmentsToTripResult>> Add(Trip trip, Shipment shipment) =>
        await new AddShipmentsToTripHandler(_fixture.Trips, _fixture.Checks, _fixture.TripShipments, _fixture.Amendments)
            .Handle(new AddShipmentsToTripCommand(trip.Number.Value, [shipment.Number.Value], "corr-add"), CancellationToken.None);
}

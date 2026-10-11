using Ardalis.Result;
using Norvia.Tms.Application.Trips.AddShipments;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Application.Trips.ReorderStops;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>
/// Amending a Dispatched Trip (P11): the Planner's edits of a Draft work on a Dispatched Trip too, and each one that changes it raises the version and
/// raises one TripDispatched with the whole Trip under the Amend's own correlation ID. A refused Amend leaves the Trip and its version as they were.
/// </summary>
public sealed class AmendDispatchedTripTests
{
    private static readonly DateOnly Tuesday = TripsFixture.Tuesday;

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task ChangingTheDriver_RaisesVersion2_AndOneTripDispatchedWithTheWholeTripUnderTheAmendsCorrelationId()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Trips.ClearReceivedCalls();

        var result = await Change(trip, Van, Joost, new TimeOnly(9, 0), TimeSpan.FromHours(4), "corr-amend");

        result.IsSuccess.Should().BeTrue();
        trip.Version.Should().Be(2);
        trip.DriverId.Should().Be(Joost);
        trip.Deliveries.Select(delivery => (delivery.Version, delivery.State)).Should().Equal((1, TripDeliveryState.Sending), (2, TripDeliveryState.Sending));
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripDispatchedEvent>().Subject;
        raised.Trip.Version.Should().Be(2);
        raised.CorrelationId.Value.Should().Be("corr-amend");
        raised.Driver.Id.Should().Be(Joost);
        raised.Shipments.Should().ContainSingle();
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachAmend_RaisesTheVersionOnce_UnderItsOwnCorrelationId()
    {
        var trip = await _fixture.DispatchedAsync();

        await Change(trip, Van, Joost, new TimeOnly(9, 0), TimeSpan.FromHours(4), "corr-2");
        await Change(trip, Van, Joost, new TimeOnly(10, 0), TimeSpan.FromHours(4), "corr-3");

        trip.Version.Should().Be(3);
        trip.DomainEvents.OfType<TripDispatchedEvent>().Select(raised => (raised.Trip.Version, raised.CorrelationId.Value)).Should().Equal((3, "corr-2"), (3, "corr-3"));
    }

    [Fact]
    public async Task AChangeThatChangesNothing_RaisesNoVersionAndNoEvent()
    {
        var trip = await _fixture.DispatchedAsync();

        (await Change(trip, Van, Anouk, new TimeOnly(9, 0), TimeSpan.FromHours(4), "corr-amend")).IsSuccess.Should().BeTrue();

        trip.Version.Should().Be(1);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ARefusedAmend_LeavesTheTripAndItsVersionUnchanged_AndRaisesNothing()
    {
        var trip = await _fixture.DispatchedAsync();
        _fixture.Existing(Tuesday, Truck, Joost);
        _fixture.Trips.ClearReceivedCalls();

        var result = await Change(trip, Van, Joost, new TimeOnly(9, 0), TimeSpan.FromHours(4), "corr-amend");

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.DriverNotAvailable));
        trip.Version.Should().Be(1);
        trip.DriverId.Should().Be(Anouk);
        trip.DomainEvents.Should().BeEmpty();
        trip.Deliveries.Should().ContainSingle();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAmendThatLeavesOutAPart_IsRefusedWithTripIncomplete_AndNothingChanges()
    {
        var trip = await _fixture.DispatchedAsync();

        var result = await Change(trip, null, Anouk, null, TimeSpan.FromHours(4), "corr-amend");

        Codes(result).Should().Equal(nameof(TripRefusalCode.TripIncomplete), nameof(TripRefusalCode.TripIncomplete));
        trip.Version.Should().Be(1);
        trip.VehicleId.Should().Be(Van);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task AddingAShipment_MakesItDispatched_AndRaisesVersion2WithBothShipments()
    {
        var trip = await _fixture.DispatchedAsync();
        var added = _fixture.ReleasedShipment(Tuesday);

        var result = await Add(trip, "corr-add", added);

        result.IsSuccess.Should().BeTrue();
        added.Status.Should().Be(ShipmentStatus.Dispatched);
        trip.Version.Should().Be(2);
        trip.Stops.Should().HaveCount(4);
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripDispatchedEvent>().Subject;
        raised.CorrelationId.Value.Should().Be("corr-add");
        raised.Shipments.Select(item => item.Shipment.Id).Should().HaveCount(2).And.Contain(added.Id);
    }

    [Fact]
    public async Task AddingAShipmentThatBreaksARule_IsRefused_ItStaysReleased_AndTheVersionIsUnchanged()
    {
        var trip = await _fixture.DispatchedAsync();
        var heavy = _fixture.ReleasedShipment(Tuesday, pallets: 2, weightKg: 50_000m);

        var result = await Add(trip, "corr-add", heavy);

        Codes(result).Should().Contain(nameof(TripAvailabilityCode.PayloadExceeded));
        heavy.Status.Should().Be(ShipmentStatus.Released);
        trip.Version.Should().Be(1);
        trip.Stops.Should().HaveCount(2);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task AddingAShipmentAlreadyOnTheTrip_RaisesNoVersion()
    {
        var trip = await _fixture.DispatchedAsync();
        var onTrip = _fixture.StoredShipments.Single();

        (await Add(trip, "corr-add", onTrip)).IsSuccess.Should().BeTrue();

        trip.Version.Should().Be(1);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ReorderingTheStops_RaisesVersion2WithTheStopsInTheNewOrder()
    {
        var trip = await _fixture.DispatchedAsync();
        var first = _fixture.StoredShipments.Single();
        var second = _fixture.ReleasedShipment(Tuesday);
        await Add(trip, "corr-add", second);
        trip.ClearDomainEvents();

        var result = await Reorder(trip, "corr-reorder", (second, StopKind.Collection), (first, StopKind.Collection), (first, StopKind.Delivery), (second, StopKind.Delivery));

        result.IsSuccess.Should().BeTrue();
        trip.Version.Should().Be(3);
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripDispatchedEvent>().Subject;
        raised.CorrelationId.Value.Should().Be("corr-reorder");
        raised.Trip.Stops[0].ShipmentId.Should().Be(second.Id);
    }

    [Fact]
    public async Task ReorderingToTheSameOrder_RaisesNoVersion_AndARefusedOrderLeavesTheTripAsItWas()
    {
        var trip = await _fixture.DispatchedAsync();
        var shipment = _fixture.StoredShipments.Single();

        (await Reorder(trip, "corr-reorder", (shipment, StopKind.Collection), (shipment, StopKind.Delivery))).IsSuccess.Should().BeTrue();
        Codes(await Reorder(trip, "corr-reorder", (shipment, StopKind.Delivery), (shipment, StopKind.Collection))).Should().Equal(nameof(TripRefusalCode.CollectionBeforeDelivery));

        trip.Version.Should().Be(1);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheDriverIsNoLongerActive_ReorderingIsRefusedWithVehicleOrDriverInactive_AndNothingChanges()
    {
        var trip = await _fixture.DispatchedAsync();
        var shipment = _fixture.StoredShipments.Single();
        _fixture.Planning.Drivers.ListActive().Returns([]);

        Codes(await Reorder(trip, "corr-reorder", (shipment, StopKind.Collection), (shipment, StopKind.Delivery))).Should().Equal(nameof(TripRefusalCode.VehicleOrDriverInactive));
        trip.Version.Should().Be(1);
    }

    [Fact]
    public async Task ADraft_IsEditedAsBefore_WithoutAVersionOrAnEvent()
    {
        var draft = _fixture.Existing(Tuesday, null, null);

        (await Change(draft, Van, Anouk, new TimeOnly(9, 0), TimeSpan.FromHours(4), "corr-ignored")).IsSuccess.Should().BeTrue();

        draft.Version.Should().Be(0);
        draft.DomainEvents.Should().BeEmpty();
    }

    private static string[] Codes(IResult result) => [.. result.ValidationErrors.Select(error => error.ErrorCode)];

    private async Task<Result> Change(Trip trip, VehicleId? vehicle, DriverId? driver, TimeOnly? start, TimeSpan? duration, string correlationId) =>
        await new ChangeDraftTripHandler(_fixture.Trips, _fixture.Checks, _fixture.TripShipments, _fixture.Amendments)
            .Handle(new ChangeDraftTripCommand(trip.Number.Value, vehicle, driver, start, duration, correlationId), CancellationToken.None);

    private async Task<Result<AddShipmentsToTripResult>> Add(Trip trip, string correlationId, params Shipment[] shipments) =>
        await new AddShipmentsToTripHandler(_fixture.Trips, _fixture.Checks, _fixture.TripShipments, _fixture.Amendments)
            .Handle(new AddShipmentsToTripCommand(trip.Number.Value, [.. shipments.Select(shipment => shipment.Number.Value)], correlationId), CancellationToken.None);

    private async Task<Result> Reorder(Trip trip, string correlationId, params (Shipment Shipment, StopKind Kind)[] order) =>
        await new ReorderTripStopsHandler(_fixture.Trips, _fixture.TripShipments, _fixture.Amendments).Handle(
            new ReorderTripStopsCommand(trip.Number.Value, [.. order.Select(stop => new StopReference(stop.Shipment.Number.Value, stop.Kind))], correlationId), CancellationToken.None);
}

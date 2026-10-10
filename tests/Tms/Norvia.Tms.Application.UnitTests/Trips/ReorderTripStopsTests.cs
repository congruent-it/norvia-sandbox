using Ardalis.Result;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.AddShipments;
using Norvia.Tms.Application.Trips.ReorderStops;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>Putting the Stops of a Draft Trip in order (P11).</summary>
public sealed class ReorderTripStopsTests
{
    private static readonly DateOnly Tuesday = TripsFixture.Tuesday;

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_ANewOrder_IsSaved()
    {
        var (trip, first, second) = await TripWithTwoShipmentsAsync();
        _fixture.Trips.ClearReceivedCalls();

        var result = await Reorder(trip, (second, StopKind.Collection), (first, StopKind.Collection), (first, StopKind.Delivery), (second, StopKind.Delivery));

        result.IsSuccess.Should().BeTrue();
        trip.Stops.Select(stop => (stop.ShipmentId, stop.Kind)).Should().Equal(
            (second.Id, StopKind.Collection), (first.Id, StopKind.Collection), (first.Id, StopKind.Delivery), (second.Id, StopKind.Delivery));
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADeliveryBeforeItsCollection_IsRefusedWithCollectionBeforeDelivery_AndTheOrderIsUnchanged()
    {
        var (trip, first, second) = await TripWithTwoShipmentsAsync();
        var before = trip.Stops.ToList();
        _fixture.Trips.ClearReceivedCalls();

        var result = await Reorder(trip, (second, StopKind.Delivery), (first, StopKind.Collection), (first, StopKind.Delivery), (second, StopKind.Collection));

        Codes(result).Should().Equal(nameof(TripRefusalCode.CollectionBeforeDelivery));
        trip.Stops.Should().Equal(before);
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TwoStopsAtTheSameAddress_StayTwoStops()
    {
        // The fixture's Shipments all collect at the same address and deliver to the same one.
        var (trip, first, second) = await TripWithTwoShipmentsAsync();

        var result = await Reorder(trip, (first, StopKind.Collection), (second, StopKind.Collection), (second, StopKind.Delivery), (first, StopKind.Delivery));

        result.IsSuccess.Should().BeTrue();
        trip.Stops.Should().HaveCount(4);
    }

    [Fact]
    public async Task Handle_AnOrderThatLeavesAStopOutOrNamesAnUnknownShipment_IsRefusedWithStopsNotTheTripsStops()
    {
        var (trip, first, _) = await TripWithTwoShipmentsAsync();
        var handler = new ReorderTripStopsHandler(_fixture.Trips, _fixture.TripShipments);

        var short_ = await Reorder(trip, (first, StopKind.Collection), (first, StopKind.Delivery));
        var unknown = await handler.Handle(
            new ReorderTripStopsCommand(trip.Number.Value, [new("SH-9999999", StopKind.Collection), new("SH-9999999", StopKind.Delivery)]), CancellationToken.None);

        Codes(short_).Should().Equal(nameof(TripRefusalCode.StopsNotTheTripsStops));
        Codes(unknown).Should().Equal(nameof(TripRefusalCode.StopsNotTheTripsStops));
    }

    [Fact]
    public async Task Handle_AnUnknownTrip_IsRefusedWithTripNotFound()
    {
        var result = await new ReorderTripStopsHandler(_fixture.Trips, _fixture.TripShipments)
            .Handle(new ReorderTripStopsCommand("TR-0009999", []), CancellationToken.None);

        Codes(result).Should().Equal(nameof(TripRefusalCode.TripNotFound));
    }

    [Fact]
    public async Task Handle_ADispatchedTrip_IsRefusedWithTripNotDraft()
    {
        var (trip, first, _) = await TripWithTwoShipmentsAsync();
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(trip, TripStatus.Dispatched);

        Codes(await Reorder(trip, (first, StopKind.Collection), (first, StopKind.Delivery))).Should().Equal(nameof(TripRefusalCode.TripNotDraft));
    }

    private async Task<(Trip Trip, Shipment First, Shipment Second)> TripWithTwoShipmentsAsync()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var (first, second) = (_fixture.ReleasedShipment(Tuesday), _fixture.ReleasedShipment(Tuesday));
        var add = new AddShipmentsToTripHandler(_fixture.Trips, _fixture.Checks, _fixture.TripShipments);
        (await add.Handle(new AddShipmentsToTripCommand(trip.Number.Value, [first.Number.Value, second.Number.Value]), CancellationToken.None)).IsSuccess.Should().BeTrue();
        return (trip, first, second);
    }

    private async Task<Result> Reorder(Trip trip, params (Shipment Shipment, StopKind Kind)[] order) =>
        await new ReorderTripStopsHandler(_fixture.Trips, _fixture.TripShipments).Handle(
            new ReorderTripStopsCommand(trip.Number.Value, [.. order.Select(stop => new StopReference(stop.Shipment.Number.Value, stop.Kind))]), CancellationToken.None);

    private static IEnumerable<string> Codes(Result result) => result.ValidationErrors.Select(error => error.ErrorCode);
}

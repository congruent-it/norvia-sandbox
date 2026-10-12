using Ardalis.Result;
using Norvia.Tms.Application.Trips.Complete;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>The Driver ends an In progress Trip (P12). Today is Monday 12 October 2026, the Trip's day is Tuesday 13 October.</summary>
public sealed class EndTripTests
{
    private static readonly DateTimeOffset TripTime = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task ADeliveredShipment_CompletesTheTrip_RaisesOneTripCompleted_AndSaves()
    {
        var (trip, shipment) = await StartedTripAsync();
        _fixture.Reported(trip, TrackingEventType.Collected, TripTime.AddHours(1));
        _fixture.Reported(trip, TrackingEventType.Delivered, TripTime.AddHours(2));
        _fixture.Trips.ClearReceivedCalls();

        var result = await End(trip.Number.Value, PlanningEditsFixture.Anouk, "corr-end");

        result.Value.Should().BeEquivalentTo(new EndTripResult(trip.Number.Value, 1, "corr-end", [shipment.Number.Value], [], []));
        trip.Status.Should().Be(TripStatus.Completed);
        trip.ShipmentIds.Should().Equal(shipment.Id);
        var completed = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripCompletedEvent>().Subject;
        completed.CorrelationId.Value.Should().Be("corr-end");
        completed.EndedAt.Should().Be(_fixture.Planning.Time.GetUtcNow());
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACollectedShipmentThatWasNotDelivered_IsAwaitingRedelivery_KeepsItsStatus_AndLeavesTheTrip()
    {
        var (trip, shipment) = await StartedTripAsync();
        _fixture.Reported(trip, TrackingEventType.Collected, TripTime.AddHours(1));
        _fixture.Reported(trip, TrackingEventType.DeliveryFailed, TripTime.AddHours(2), "Closed");

        var result = await End(trip.Number.Value, PlanningEditsFixture.Anouk, "corr-end");

        result.Value.NotDelivered.Should().Equal(shipment.Number.Value);
        shipment.Status.Should().Be(ShipmentStatus.Collected);
        shipment.AwaitingRedelivery.Should().BeTrue();
        trip.ShipmentIds.Should().BeEmpty();
        trip.DomainEvents.OfType<ShipmentTakenOffTripEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task AShipmentNotCollected_IsPlannedAgain_AndLeavesTheTrip_WithoutItsDeliveryStopNeedingAnOutcome()
    {
        var (trip, shipment) = await StartedTripAsync();
        _fixture.Reported(trip, TrackingEventType.CollectionFailed, TripTime.AddHours(1), "Not ready");

        var result = await End(trip.Number.Value, PlanningEditsFixture.Anouk, "corr-end");

        result.Value.NotCollected.Should().Equal(shipment.Number.Value);
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        shipment.AwaitingRedelivery.Should().BeFalse();
        trip.ShipmentIds.Should().BeEmpty();
        trip.DomainEvents.OfType<ShipmentTakenOffTripEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task AStopWithoutAnOutcome_IsRefusedWithStopsOpen_AndNothingChanges()
    {
        var (trip, shipment) = await StartedTripAsync();
        _fixture.Reported(trip, TrackingEventType.Collected, TripTime.AddHours(1));
        _fixture.Trips.ClearReceivedCalls();

        var result = await End(trip.Number.Value, PlanningEditsFixture.Anouk, "corr-end");

        Codes(result).Should().Equal(nameof(TripRefusalCode.StopsOpen));
        trip.Status.Should().Be(TripStatus.InProgress);
        trip.DomainEvents.Should().BeEmpty();
        shipment.AwaitingRedelivery.Should().BeFalse();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherDriver_AndAnUnknownTrip_AreRefusedWithTripNotFound()
    {
        var (trip, _) = await StartedTripAsync();

        Codes(await End(trip.Number.Value, PlanningEditsFixture.Joost, "corr-end")).Should().Equal(nameof(TripRefusalCode.TripNotFound));
        Codes(await End("TR-0009999", PlanningEditsFixture.Anouk, "corr-end")).Should().Equal(nameof(TripRefusalCode.TripNotFound));
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public async Task ADispatchedTrip_AndATripAlreadyEnded_AreRefusedWithTripNotUnderWay()
    {
        var dispatched = await _fixture.DispatchedAsync();
        var (ended, _) = await StartedTripAsync();
        _fixture.Reported(ended, TrackingEventType.CollectionFailed, TripTime.AddHours(1));
        (await End(ended.Number.Value, PlanningEditsFixture.Anouk, "corr-end")).IsSuccess.Should().BeTrue();
        ended.ClearDomainEvents();
        _fixture.Trips.ClearReceivedCalls();

        Codes(await End(dispatched.Number.Value, PlanningEditsFixture.Anouk, "corr-end")).Should().Equal(nameof(TripRefusalCode.TripNotUnderWay));
        Codes(await End(ended.Number.Value, PlanningEditsFixture.Anouk, "corr-again")).Should().Equal(nameof(TripRefusalCode.TripNotUnderWay));
        ended.DomainEvents.Should().BeEmpty();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private async Task<(Trip Trip, Shipment Shipment)> StartedTripAsync()
    {
        var trip = await _fixture.DispatchedAsync();
        trip.Start(new Norvia.Tms.Domain.TransportOrders.CorrelationId("corr-start"), TripTime);
        trip.ClearDomainEvents();
        var shipment = _fixture.StoredShipments.Single(candidate => trip.HasShipment(candidate.Id));
        _fixture.Planning.Time.SetUtcNow(TripTime.AddHours(8));
        return (trip, shipment);
    }

    private async Task<Result<EndTripResult>> End(string number, DriverId driver, string correlationId) =>
        await new EndTripHandler(_fixture.Trips, _fixture.Shipments, _fixture.Outcomes, _fixture.Planning.Time)
            .Handle(new EndTripCommand(number, driver.Value, correlationId), CancellationToken.None);

    private static IEnumerable<string> Codes(Result<EndTripResult> result) => result.ValidationErrors.Select(error => error.ErrorCode);
}

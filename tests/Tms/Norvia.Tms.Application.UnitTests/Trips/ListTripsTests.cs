using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.List;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.Trips;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class ListTripsTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_ListsEveryTripByDayWithItsVehicleAndDriver_AlsoOneOfADayThatHasPassed()
    {
        _fixture.Existing(TripsFixture.Tuesday.AddDays(1), null, null);
        _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);
        _fixture.Existing(Monday.AddDays(-1), null, null);

        var trips = await new ListTripsHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck, _fixture.Outcomes)
            .Handle(new ListTripsQuery(), CancellationToken.None);

        trips.Select(trip => trip.Day).Should().Equal(Monday.AddDays(-1), TripsFixture.Tuesday, TripsFixture.Tuesday.AddDays(1));
        trips[1].Should().Be(new TripView("TR-0000101", TripsFixture.Tuesday, TripStatus.Draft, "VEH-0002", "VV-102-B", "DRV-0001", "DRV-0001", null, null, PayloadKg: 1200, VehiclePalletPlaces: 4));
        trips[0].VehicleId.Should().BeNull();
        trips[0].DriverName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ATripUnderWay_CountsTheStopsWithAnOutcome_AndDamageIsNone()
    {
        var trip = await _fixture.DispatchedAsync();
        trip.Start(new Norvia.Tms.Domain.TransportOrders.CorrelationId("corr-start"), _fixture.Planning.Time.GetUtcNow());
        var at = _fixture.Planning.Time.GetUtcNow();
        _fixture.Reported(trip, TrackingEventType.Damaged, at);
        var before = await ListAsync();
        _fixture.Reported(trip, TrackingEventType.Collected, at.AddMinutes(1));
        _fixture.Reported(trip, TrackingEventType.DeliveryFailed, at.AddMinutes(2));

        var after = await ListAsync();

        before.Single().StopsDone.Should().Be(0);
        after.Single().Should().Match<TripView>(view => view.StopsDone == 2 && view.StopCount == 2 && view.Status == TripStatus.InProgress);
    }

    private async Task<IReadOnlyList<TripView>> ListAsync() =>
        await new ListTripsHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck, _fixture.Outcomes)
            .Handle(new ListTripsQuery(), CancellationToken.None);

    [Fact]
    public async Task Handle_ShowsTheStopCountAndTheLoadAgainstTheVehiclesCapacity()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, null);
        trip.AddShipment(_fixture.ReleasedShipment(TripsFixture.Tuesday, pallets: 2, weightKg: 400m).Id);
        trip.AddShipment(_fixture.ReleasedShipment(TripsFixture.Tuesday, pallets: 1, weightKg: 250m).Id);
        _fixture.Existing(TripsFixture.Tuesday.AddDays(1), null, null);

        var trips = await new ListTripsHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck, _fixture.Outcomes)
            .Handle(new ListTripsQuery(), CancellationToken.None);

        trips[0].Should().Match<TripView>(view => view.StopCount == 4 && view.LoadWeightKg == 650m && view.LoadPalletPlaces == 3 && view.PayloadKg == 1200 && view.VehiclePalletPlaces == 4);
        trips[1].Should().Match<TripView>(view => view.StopCount == 0 && view.LoadWeightKg == 0m && view.PayloadKg == null && view.VehiclePalletPlaces == null);
    }

    [Fact]
    public async Task Handle_ShowsTheDeliveryStateOfTheLatestVersion_AndNoneForADraft()
    {
        var dispatched = await _fixture.DispatchedAsync();
        _fixture.Existing(TripsFixture.Tuesday.AddDays(1), null, null);

        var trips = await new ListTripsHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck, _fixture.Outcomes)
            .Handle(new ListTripsQuery(), CancellationToken.None);

        trips.Single(trip => trip.Number == dispatched.Number.Value).LatestDelivery.Should().Be(TripDeliveryState.Sending);
        dispatched.ReportDelivered(1, _fixture.Planning.Time.GetUtcNow());
        (await new ListTripsHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck, _fixture.Outcomes)
            .Handle(new ListTripsQuery(), CancellationToken.None)).Single(trip => trip.Number == dispatched.Number.Value).LatestDelivery.Should().Be(TripDeliveryState.Delivered);
        trips.Single(trip => trip.Status == TripStatus.Draft).LatestDelivery.Should().BeNull();
    }
}

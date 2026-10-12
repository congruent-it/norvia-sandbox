using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class GetTripTests
{
    private static readonly DateOnly Tuesday = TripsFixture.Tuesday;

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_ShowsTheStopsInOrderWithTheirPlacesAndWindows_DerivedFromTheShipments()
    {
        var trip = _fixture.Existing(Tuesday, Van, null);
        var first = _fixture.ReleasedShipment(Tuesday, pallets: 2, weightKg: 400m);
        var second = _fixture.ReleasedShipment(Tuesday, pallets: 1, weightKg: 250m);
        trip.AddShipment(first.Id);
        trip.AddShipment(second.Id);

        var result = await Get(trip);

        var stops = result.Value.Stops;
        stops.Select(stop => (stop.Position, stop.Kind, stop.ShipmentNumber)).Should().Equal(
            (0, StopKind.Collection, first.Number.Value),
            (1, StopKind.Delivery, first.Number.Value),
            (2, StopKind.Collection, second.Number.Value),
            (3, StopKind.Delivery, second.Number.Value));
        stops[0].Place.Should().Be("Waalhaven Oostzijde 88, 3087 BM Rotterdam");
        stops[1].Place.Should().Be("Oudegracht 112, 3511 AW Utrecht");
        stops[1].Name.Should().Be("Café De Zwaan");
        stops[0].Goods.Should().Be("2 pallet");
        stops[0].WindowLatest.Should().BeAfter(stops[0].WindowEarliest!.Value);
        result.Value.Trip.Should().Match<TripView>(view => view.StopCount == 4 && view.LoadWeightKg == 650m && view.PayloadKg == 1200);
        result.Value.Failures.Should().BeEmpty();
        result.Value.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShowsTheRuleFailuresOfTheTripAsItStands_AndTheWarnings()
    {
        var trip = _fixture.Existing(Tuesday, Van, null);
        var heavy = _fixture.ReleasedShipment(Tuesday, pallets: 1, weightKg: 1_500m, deliveryDay: Tuesday.AddDays(1));
        trip.AddShipment(heavy.Id);

        var result = await Get(trip);

        result.Value.Failures.Select(failure => failure.Code).Should().Equal(nameof(TripAvailabilityCode.PayloadExceeded));
        result.Value.Warnings.Should().ContainSingle().Which.Code.Should().Be(TripWarning.DeliveryWindowOutsideTripDay);
    }

    [Fact]
    public async Task Handle_ADispatchedTrip_ListsEveryVersionWithItsDeliveryState_AndTheLatestOnTheTrip()
    {
        var trip = await _fixture.DispatchedAsync();
        trip.ReportNotDelivered(1, "The Driver app answered 503.", new Norvia.Tms.Domain.TransportOrders.CorrelationId("corr-report"), _fixture.Planning.Time.GetUtcNow());

        var result = await Get(trip);

        result.Value.Versions.Should().Equal(new TripVersionView(1, TripDeliveryState.NotDelivered, "The Driver app answered 503.", CanSendAgain: true));
        result.Value.Trip.LatestDelivery.Should().Be(TripDeliveryState.NotDelivered);
    }

    [Fact]
    public async Task Handle_ADraft_HasNoVersions()
    {
        var result = await Get(_fixture.Existing(Tuesday, Van, null));

        result.Value.Versions.Should().BeEmpty();
        result.Value.Trip.LatestDelivery.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AnUnknownTrip_IsRefusedWithTripNotFound()
    {
        var result = await new GetTripHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Checks, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck)
            .Handle(new GetTripQuery("TR-0009999"), CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(TripRefusalCode.TripNotFound));
    }

    private async Task<Ardalis.Result.Result<TripDetailView>> Get(Trip trip) =>
        await new GetTripHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Checks, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck)
            .Handle(new GetTripQuery(trip.Number.Value), CancellationToken.None);
}

using Norvia.Tms.Application.Shipments.ListToPutOnTrips;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class ListShipmentsToPutOnTripsTests
{
    private static readonly DateOnly Tuesday = TripsFixture.Tuesday;

    private readonly TripsFixture _fixture = new();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();

    [Fact]
    public async Task Handle_ListsReleasedOwnFleetShipmentsNotOnATrip_ByCollectionTime()
    {
        var onTrip = _fixture.ReleasedShipment(Tuesday);
        var later = _fixture.ReleasedShipment(Tuesday.AddDays(1), pallets: 3, weightKg: 900m);
        var earlier = _fixture.ReleasedShipment(Tuesday);
        _fixture.ReleasedShipment(Tuesday, release: false);
        _fixture.ReleasedShipment(Tuesday, carriedBy: CarriedBy.Carrier(new CarrierId("CAR-0001")));
        _fixture.Existing(Tuesday, null, null).AddShipment(onTrip.Id);

        var list = await List(null);

        list.Select(item => item.ShipmentNumber).Should().Equal(earlier.Number.Value, later.Number.Value);
        list[1].Should().Match<ShipmentToPutOnTripSummary>(item =>
            item.CollectionDay == Tuesday.AddDays(1) && item.WeightKg == 900m && item.PalletPlaces == 3
            && item.CollectionPlace == "Waalhaven Oostzijde 88, 3087 BM Rotterdam" && item.DeliveryPlace == "Oudegracht 112, 3511 AW Utrecht");
    }

    [Fact]
    public async Task Handle_FilteredByDay_ListsOnlyThoseCollectingThatDay()
    {
        var onDay = _fixture.ReleasedShipment(Tuesday);
        _fixture.ReleasedShipment(Tuesday.AddDays(1));

        var list = await List(Tuesday);

        list.Select(item => item.ShipmentNumber).Should().Equal(onDay.Number.Value);
    }

    [Fact]
    public async Task Handle_ListsAShipmentAwaitingRedelivery_MarkedRedelivery_OnEveryDayAndNotOnceItIsOnATrip()
    {
        var released = _fixture.ReleasedShipment(Tuesday.AddDays(1));
        var redelivery = _fixture.AwaitingRedeliveryShipment(Tuesday);
        var onTrip = _fixture.AwaitingRedeliveryShipment(Tuesday);
        _fixture.Existing(Tuesday.AddDays(3), null, null).AddShipment(onTrip.Id, redelivery: true);

        var all = await List(null);
        var filtered = await List(Tuesday.AddDays(1));

        all.Select(item => (item.ShipmentNumber, item.Redelivery)).Should().Equal((redelivery.Number.Value, true), (released.Number.Value, false));
        filtered.Select(item => item.ShipmentNumber).Should().Equal(redelivery.Number.Value, released.Number.Value);
    }

    private async Task<IReadOnlyList<ShipmentToPutOnTripSummary>> List(DateOnly? day) =>
        await new ListShipmentsToPutOnTripsHandler(_fixture.Shipments.AsRead(), _fixture.Trips.AsRead(), _fixture.TripShipments, _shippers)
            .Handle(new ListShipmentsToPutOnTripsQuery(day), CancellationToken.None);
}

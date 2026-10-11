using Ardalis.Result;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.AddShipments;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>
/// Putting Shipments on a Draft Trip (P11). The Truck carries 10,000 kg and 18 pallets, the Van 1,200 kg and 4 pallets. A Shipment of the
/// tests collects on Tuesday 13 October 2026 unless it says otherwise.
/// </summary>
public sealed class AddShipmentsToTripTests
{
    private static readonly DateOnly Tuesday = TripsFixture.Tuesday;

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_ReleasedOwnFleetShipments_PutTheirCollectionAndDeliveryStopsAtTheEnd_AndLeaveThemReleased()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var first = _fixture.ReleasedShipment(Tuesday);
        var second = _fixture.ReleasedShipment(Tuesday);

        var result = await Add(trip, first, second);

        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().BeEmpty();
        trip.Stops.Select(stop => (stop.ShipmentId, stop.Kind)).Should().Equal(
            (first.Id, StopKind.Collection), (first.Id, StopKind.Delivery), (second.Id, StopKind.Collection), (second.Id, StopKind.Delivery));
        first.Status.Should().Be(ShipmentStatus.Released);
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ASecondAdd_PutsItsStopsAfterTheFirst()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var first = _fixture.ReleasedShipment(Tuesday);
        var second = _fixture.ReleasedShipment(Tuesday);
        await Add(trip, first);

        await Add(trip, second);

        trip.Stops.Select(stop => stop.ShipmentId).Should().Equal(first.Id, first.Id, second.Id, second.Id);
        trip.Stops.Select(stop => stop.Position).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public async Task Handle_ShipmentsAlreadyOnTheTrip_AreAcceptedAndChangeNothing()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var shipment = _fixture.ReleasedShipment(Tuesday);
        await Add(trip, shipment);

        var again = await Add(trip, shipment);

        again.IsSuccess.Should().BeTrue();
        trip.Stops.Should().HaveCount(2);
        await _fixture.Trips.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_APlannedShipment_IsRefusedWithShipmentNotReleased_AndTheTripIsUnchanged()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var planned = _fixture.ReleasedShipment(Tuesday, release: false);

        var result = await Add(trip, planned);

        Codes(result).Should().Equal(nameof(TripRefusalCode.ShipmentNotReleased));
        trip.Stops.Should().BeEmpty();
        await _fixture.Trips.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ACarrierShipment_IsRefusedWithShipmentNotReleased()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var carrier = _fixture.ReleasedShipment(Tuesday, carriedBy: CarriedBy.Carrier(new CarrierId("CAR-0001")));

        Codes(await Add(trip, carrier)).Should().Equal(nameof(TripRefusalCode.ShipmentNotReleased));
    }

    [Fact]
    public async Task Handle_AShipmentOnAnotherTrip_IsRefusedWithShipmentOnAnotherTrip_AndTheTripIsUnchanged()
    {
        var other = _fixture.Existing(Tuesday, Van, Joost);
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var shipment = _fixture.ReleasedShipment(Tuesday);
        await Add(other, shipment);

        var result = await Add(trip, shipment);

        Codes(result).Should().Equal(nameof(TripRefusalCode.ShipmentOnAnotherTrip));
        trip.Stops.Should().BeEmpty();
        other.Stops.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_AShipmentThatCollectsOnAnotherDay_IsRefusedWithCollectionDayMismatch()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var wrongDay = _fixture.ReleasedShipment(Tuesday.AddDays(1));

        var result = await Add(trip, wrongDay);

        Codes(result).Should().Equal(nameof(TripRefusalCode.CollectionDayMismatch));
        trip.Stops.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AShipmentTooHeavyForTheVehicle_IsRefusedWithPayloadExceeded()
    {
        var trip = _fixture.Existing(Tuesday, Van, null);
        var heavy = _fixture.ReleasedShipment(Tuesday, pallets: 1, weightKg: 1_500m);

        var result = await Add(trip, heavy);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.PayloadExceeded));
        trip.Stops.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MorePalletsThanTheVehicleHasPlacesFor_IsRefusedWithPalletPlacesExceeded()
    {
        var trip = _fixture.Existing(Tuesday, Van, null);
        var many = _fixture.ReleasedShipment(Tuesday, pallets: 5, weightKg: 100m);

        Codes(await Add(trip, many)).Should().Equal(nameof(TripAvailabilityCode.PalletPlacesExceeded));
    }

    [Fact]
    public async Task Handle_TheLoadCountsTheWholeTripAtOnce_SoTwoShipmentsThatFitAloneAreRefusedTogether_AndNeitherIsAdded()
    {
        var trip = _fixture.Existing(Tuesday, Van, null);
        var first = _fixture.ReleasedShipment(Tuesday, pallets: 2, weightKg: 700m);
        var second = _fixture.ReleasedShipment(Tuesday, pallets: 2, weightKg: 700m);
        (await Add(trip, first)).IsSuccess.Should().BeTrue();

        var result = await Add(trip, second);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.PayloadExceeded));
        trip.ShipmentIds.Should().Equal(first.Id);
    }

    [Fact]
    public async Task Handle_ABatchWithOneShipmentThatBreaksARule_AddsNone()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var good = _fixture.ReleasedShipment(Tuesday);
        var wrongDay = _fixture.ReleasedShipment(Tuesday.AddDays(2));

        var result = await Add(trip, good, wrongDay);

        Codes(result).Should().Equal(nameof(TripRefusalCode.CollectionDayMismatch));
        trip.Stops.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ATripWithoutAVehicle_HasNoCapacityToExceed()
    {
        var trip = _fixture.Existing(Tuesday, null, null);

        (await Add(trip, _fixture.ReleasedShipment(Tuesday, pallets: 40, weightKg: 90_000m))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ADeliveryWindowThatDoesNotIncludeTheTripsDay_IsAcceptedWithAWarning()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        var late = _fixture.ReleasedShipment(Tuesday, deliveryDay: Tuesday.AddDays(1));
        var onTime = _fixture.ReleasedShipment(Tuesday);

        var result = await Add(trip, late, onTime);

        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().ContainSingle().Which.Should().Be(TripWarning.DeliveryWindowOutside(late.Number.Value, Tuesday));
        trip.ShipmentIds.Should().Equal(late.Id, onTime.Id);
    }

    [Fact]
    public async Task Handle_AnUnknownShipment_IsRefusedWithShipmentNotFound()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);

        var result = await _handlerFor().Handle(new AddShipmentsToTripCommand(trip.Number.Value, ["SH-0009999", "nonsense"]), CancellationToken.None);

        Codes(result).Should().Equal(nameof(TripRefusalCode.ShipmentNotFound), nameof(TripRefusalCode.ShipmentNotFound));
    }

    [Fact]
    public async Task Handle_AnUnknownTrip_IsRefusedWithTripNotFound()
    {
        var result = await _handlerFor().Handle(new AddShipmentsToTripCommand("TR-0009999", ["SH-0000001"]), CancellationToken.None);

        Codes(result).Should().Equal(nameof(TripRefusalCode.TripNotFound));
    }

    [Fact]
    public async Task Handle_ATripUnderWay_IsRefusedWithTripUnderWay()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(trip, TripStatus.InProgress);

        Codes(await Add(trip, _fixture.ReleasedShipment(Tuesday))).Should().Equal(nameof(TripRefusalCode.TripUnderWay));
    }

    [Fact]
    public async Task ChangingTheVehicleOfATripWithShipments_ChecksTheLoadAgainstTheNewVehicle()
    {
        var trip = _fixture.Existing(Tuesday, Truck, null);
        await Add(trip, _fixture.ReleasedShipment(Tuesday, pallets: 2, weightKg: 3_000m));

        var change = new ChangeTripHandler(_fixture.Trips, _fixture.Checks, _fixture.TripShipments, _fixture.Amendments);
        var result = await change.Handle(new ChangeTripCommand(trip.Number.Value, Van, null), CancellationToken.None);

        Codes(result).Should().Equal(nameof(TripAvailabilityCode.PayloadExceeded));
        trip.VehicleId.Should().Be(Truck);
    }

    [Fact]
    public async Task CreatingATripFromShipments_PutsTheirStopsOnIt_OrCreatesNothing()
    {
        var create = new CreateDraftTripHandler(_fixture.Trips, _fixture.Numbers, _fixture.Checks, _fixture.TripShipments);
        var first = _fixture.ReleasedShipment(Tuesday);
        var second = _fixture.ReleasedShipment(Tuesday);
        var wrongDay = _fixture.ReleasedShipment(Tuesday.AddDays(1));

        var refused = await create.Handle(new CreateDraftTripCommand(Tuesday, Truck, null, new("k1"), ShipmentNumbers: [first.Number.Value, wrongDay.Number.Value]), CancellationToken.None);
        _fixture.Stored.Should().BeEmpty("a refusal creates nothing");
        var created = await create.Handle(new CreateDraftTripCommand(Tuesday, Truck, null, new("k2"), ShipmentNumbers: [first.Number.Value, second.Number.Value]), CancellationToken.None);

        Codes(refused).Should().Equal(nameof(TripRefusalCode.CollectionDayMismatch));
        created.IsSuccess.Should().BeTrue();
        _fixture.Stored.Single().ShipmentIds.Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task CreatingATripAgainUnderTheSameKey_IsAnsweredWithTheSameWarnings()
    {
        var create = new CreateDraftTripHandler(_fixture.Trips, _fixture.Numbers, _fixture.Checks, _fixture.TripShipments);
        var late = _fixture.ReleasedShipment(Tuesday, deliveryDay: Tuesday.AddDays(1));
        var command = new CreateDraftTripCommand(Tuesday, Truck, null, new("k-retry"), ShipmentNumbers: [late.Number.Value]);

        var first = await create.Handle(command, CancellationToken.None);
        var again = await create.Handle(command, CancellationToken.None);

        _fixture.Stored.Should().ContainSingle();
        again.Value.Number.Should().Be(first.Value.Number);
        again.Value.Warnings.Should().Equal(first.Value.Warnings!).And.HaveCount(1);
    }

    private AddShipmentsToTripHandler _handlerFor() => new(_fixture.Trips, _fixture.Checks, _fixture.TripShipments, _fixture.Amendments);

    private async Task<Result<AddShipmentsToTripResult>> Add(Trip trip, params Shipment[] shipments) =>
        await _handlerFor().Handle(new AddShipmentsToTripCommand(trip.Number.Value, [.. shipments.Select(shipment => shipment.Number.Value)]), CancellationToken.None);

    private static IEnumerable<string> Codes(Ardalis.Result.IResult result) => result.ValidationErrors.Select(error => error.ErrorCode);
}

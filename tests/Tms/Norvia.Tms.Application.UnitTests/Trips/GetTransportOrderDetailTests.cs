using Ardalis.Result;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders.GetDetail;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class GetTransportOrderDetailTests
{
    private static readonly DateOnly Tuesday = TripsFixture.Tuesday;
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly TripsFixture _fixture = new();
    private readonly List<CarrierBooking> _bookings = [];
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();

    public GetTransportOrderDetailTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
    }

    [Fact]
    public async Task Handle_AnUnknownOrMalformedNumber_IsNotFound()
    {
        (await Get("TO-0009999")).Status.Should().Be(ResultStatus.NotFound);
        (await Get("not-a-number")).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_AnAcceptedOrder_HasNoShipmentAndNoTrip()
    {
        var order = Norvia.Tms.Application.UnitTests.TransportOrders.Samples.AcceptedOrder(sequence: 77);
        _fixture.StoredOrders.Add(order);

        var detail = (await Get(order.Number!.Value)).Value;

        detail.TransportOrder.TransportOrderNumber.Should().Be(order.Number.Value);
        detail.Shipment.Should().BeNull();
        detail.Trip.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AnOwnFleetShipmentOnNoTrip_ShowsTheShipmentCarriedByOwnFleetAndNoTripOrBooking()
    {
        var shipment = _fixture.ReleasedShipment(Tuesday);

        var detail = (await Get(OrderOf(shipment))).Value;

        detail.Shipment.Should().Be(new TransportOrderShipmentView(shipment.Number.Value, ShipmentStatus.Released, new CarriedByView(AssignedTo.OwnFleet, null), null));
        detail.Trip.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ACarrierShipment_ShowsTheCarrierAndItsOpenBookingBeforeAnEarlierEndedOne()
    {
        var shipment = _fixture.ReleasedShipment(Tuesday, carriedBy: CarriedBy.Carrier(VanDijk));
        var ended = Booking(shipment, sequence: 1, requestedAt: Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now);
        ended.Reject(CarrierBookingRejectionReason.NoCapacity, null, new CorrelationId("corr-answer"), Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now.AddMinutes(5));
        var open = Booking(shipment, sequence: 2, requestedAt: Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now.AddHours(1));
        var later = Booking(shipment, sequence: 3, requestedAt: Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now.AddHours(2));
        later.Reject(CarrierBookingRejectionReason.NoCapacity, null, new CorrelationId("corr-answer"), Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now.AddHours(3));

        var detail = (await Get(OrderOf(shipment))).Value;

        detail.Shipment!.CarriedBy.Should().Be(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")));
        detail.Shipment.CarrierBooking!.CarrierBookingNumber.Should().Be(open.Number.Value);
        detail.Shipment.CarrierBooking.Status.Should().Be(CarrierBookingStatus.Requested);
    }

    [Fact]
    public async Task Handle_ACarrierShipmentWhoseBookingsAllEnded_ShowsTheLatest()
    {
        var shipment = _fixture.ReleasedShipment(Tuesday, carriedBy: CarriedBy.Carrier(VanDijk));
        var first = Booking(shipment, sequence: 1, requestedAt: Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now);
        first.Reject(CarrierBookingRejectionReason.NoCapacity, null, new CorrelationId("corr-answer"), Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now.AddMinutes(5));
        var latest = Booking(shipment, sequence: 2, requestedAt: Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now.AddHours(1));
        latest.Reject(CarrierBookingRejectionReason.LaneNotServed, null, new CorrelationId("corr-answer"), Norvia.Tms.Application.UnitTests.TransportOrders.Samples.Now.AddHours(2));

        var detail = (await Get(OrderOf(shipment))).Value;

        detail.Shipment!.CarrierBooking!.CarrierBookingNumber.Should().Be(latest.Number.Value);
        detail.Shipment.CarrierBooking.Status.Should().Be(CarrierBookingStatus.Rejected);
    }

    [Fact]
    public async Task Handle_AShipmentOnATrip_ShowsTheTripAndOnlyThisOrdersStopsWithTheirOutcomes()
    {
        var trip = _fixture.Existing(Tuesday, Van, Anouk);
        var shipment = _fixture.ReleasedShipment(Tuesday);
        var other = _fixture.ReleasedShipment(Tuesday);
        trip.AddShipment(other.Id);
        trip.AddShipment(shipment.Id);
        var collectedAt = new DateTimeOffset(2026, 10, 13, 8, 30, 0, TimeSpan.Zero);
        ReportedFor(trip, shipment, TrackingEventType.Collected, collectedAt, "Dock 4");

        var detail = (await Get(OrderOf(shipment))).Value;

        detail.Trip.Should().NotBeNull();
        detail.Trip!.TripNumber.Should().Be(trip.Number.Value);
        detail.Trip.Day.Should().Be(Tuesday);
        detail.Trip.Status.Should().Be(TripStatus.Draft);
        detail.Trip.Stops.Select(stop => (stop.Kind, stop.ShipmentNumber, stop.Outcome)).Should().Equal(
            (StopKind.Collection, shipment.Number.Value, StopOutcomeKind.Collected),
            (StopKind.Delivery, shipment.Number.Value, StopOutcomeKind.Open));
        detail.Trip.Stops[0].OutcomeAt.Should().Be(collectedAt);
        detail.Trip.Stops[0].OutcomeRemark.Should().Be("Dock 4");
    }

    private void ReportedFor(Trip trip, Shipment shipment, TrackingEventType type, DateTimeOffset at, string remark)
    {
        var report = new TrackingReport(
            TrackingSource.OwnFleet, Guid.NewGuid().ToString(), null, null, type, at, new TrackingLocation("Utrecht", "3511 AW", "NL"), null, remark,
            new OwnFleetReport(trip.Number, trip.DriverId!, shipment.Number, trip.Version));
        _fixture.StoredTrackingEvents.Add(shipment.Record(report, new CorrelationId("corr-driver"), at));
    }

    private CarrierBooking Booking(Shipment shipment, long sequence, DateTimeOffset requestedAt)
    {
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(sequence), shipment.Id, shipment.Number, VanDijk, new CorrelationId($"corr-release-{sequence}"), requestedAt);
        _bookings.Add(booking);
        return booking;
    }

    private static string OrderOf(Shipment shipment) => shipment.TransportOrderNumber.Value;

    private async Task<Result<TransportOrderDetailView>> Get(string number) =>
        await new GetTransportOrderDetailHandler(
            _fixture.TransportOrders.AsRead(),
            _fixture.Shipments.AsRead(),
            InMemoryRepository.Over(_bookings).AsRead(),
            _fixture.Trips.AsRead(),
            _fixture.TripShipments,
            _fixture.Outcomes,
            _shippers,
            _carriers).Handle(new GetTransportOrderDetailQuery(number), CancellationToken.None);
}

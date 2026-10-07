using Ardalis.SharedKernel;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class ListOpenExceptionsHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly List<Shipment> _shipmentsOnFile = [];
    private readonly IReadRepository<ShipperNotification> _notifications = Substitute.For<IReadRepository<ShipperNotification>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly ListOpenExceptionsHandler _handler;

    public ListOpenExceptionsHandlerTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _notifications.ListAsync(Arg.Any<ShipperNotificationsOfExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _shipments.ListAsync(Arg.Any<ShipmentsByIdsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _shipmentsOnFile.ToList());
        _handler = new ListOpenExceptionsHandler(_exceptions, _shipments, _notifications, _shippers, _carriers);
    }

    [Fact]
    public async Task Handle_ListsEachOpenException_WithItsShipmentShipperAndWhoCarried_AndItsNumberOfCauses()
    {
        var carrier = Failed();
        carrier.AddCause(ExceptionCause.From(OpenCause(carrier, "evt-2"), Samples.Now.AddDays(2))!);
        var ownFleet = Failed(CarriedBy.OwnFleet, orderSequence: 8, shipmentSequence: 13, number: 6);
        _exceptions.ListAsync(Arg.Any<OpenExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([carrier, ownFleet]);

        var open = await _handler.Handle(new ListOpenExceptionsQuery(), CancellationToken.None);

        open.Should().BeEquivalentTo(
            [
            new OpenExceptionSummary(
                "EX-0000005",
                "SH-0000012",
                new ShipperView("SHP-0001", "Brouwer Dranken"),
                new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
                ExceptionType.DeliveryFailed,
                Samples.Now.AddDays(1),
                CanCancelCarrierBooking: true,
                CanAgreeDeliveryWindow: false,
                NumberOfCauses: 2,
                AfterDelivered: false,
                CauseCleared: false,
                Notes: [],
                LastShipperNotificationStatus: null),
            new OpenExceptionSummary(
                "EX-0000006",
                "SH-0000013",
                new ShipperView("SHP-0001", "Brouwer Dranken"),
                new CarriedByView(AssignedTo.OwnFleet, null),
                ExceptionType.DeliveryFailed,
                Samples.Now.AddDays(1),
                CanCancelCarrierBooking: false,
                CanAgreeDeliveryWindow: false,
                NumberOfCauses: 1,
                AfterDelivered: false,
                CauseCleared: false,
                Notes: [],
                LastShipperNotificationStatus: null)],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_ListsTheNotesOfAnException_InTheOrderTheyWereAdded()
    {
        var exception = Failed();
        exception.AddNote("First", "Anna", new CorrelationId("c1"), Samples.Now.AddDays(2));
        exception.AddNote("Second", "Bram", new CorrelationId("c2"), Samples.Now.AddDays(3));
        _exceptions.ListAsync(Arg.Any<OpenExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([exception]);

        var open = await _handler.Handle(new ListOpenExceptionsQuery(), CancellationToken.None);

        open.Single().Notes.Select(note => (note.Text, note.Planner, note.CorrelationId)).Should().Equal(("First", "Anna", "c1"), ("Second", "Bram", "c2"));
    }

    [Fact]
    public async Task Handle_AnExceptionWhoseCauseIsCleared_StaysListed_MarkedCauseCleared()
    {
        var cleared = Failed();
        cleared.MarkCauseCleared();
        _exceptions.ListAsync(Arg.Any<OpenExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([cleared]);

        var open = await _handler.Handle(new ListOpenExceptionsQuery(), CancellationToken.None);

        open.Should().ContainSingle().Which.CauseCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShowsTheStatusOfTheLastShipperNotification_AndNoneForAnExceptionNeverNotified()
    {
        var notified = Failed();
        var never = Failed(orderSequence: 8, shipmentSequence: 13, number: 6);
        var first = ShipperNotificationSamples.Requested(notified, Samples.Now.AddDays(2));
        first.ReportNotDelivered(503, "Down", Samples.Now.AddDays(2));
        var last = ShipperNotificationSamples.Requested(notified, Samples.Now.AddDays(3));
        last.ReportSent(Samples.Now.AddDays(3));
        _exceptions.ListAsync(Arg.Any<OpenExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([notified, never]);
        _notifications.ListAsync(Arg.Any<ShipperNotificationsOfExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([last, first]);

        var open = await _handler.Handle(new ListOpenExceptionsQuery(), CancellationToken.None);

        open.Select(summary => summary.LastShipperNotificationStatus).Should().Equal(ShipperNotificationStatus.Sent, null);
    }

    [Fact]
    public async Task Handle_WithNoOpenExceptions_ListsNone()
    {
        _exceptions.ListAsync(Arg.Any<OpenExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        (await _handler.Handle(new ListOpenExceptionsQuery(), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OffersToAgreeADeliveryWindow_OnlyForAShipmentInTransport_AndToCancelOnlyForABookedCarrierShipment()
    {
        var booked = Failed();
        var collected = Failed(orderSequence: 8, shipmentSequence: 13, number: 6);
        _shipmentsOnFile.Single(shipment => shipment.Number.Value == "SH-0000013").Record(
            new TrackingReport(
                TrackingSource.Carrier(VanDijk), "evt-col", "VDT-1", null, TrackingEventType.Collected, Samples.Now.AddDays(1),
                new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
            new CorrelationId("corr-col"),
            Samples.Now.AddDays(1));
        _exceptions.ListAsync(Arg.Any<OpenExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([booked, collected]);

        var open = await _handler.Handle(new ListOpenExceptionsQuery(), CancellationToken.None);

        open.Select(summary => (summary.CanCancelCarrierBooking, summary.CanAgreeDeliveryWindow)).Should().Equal((true, false), (false, true));
    }

    private ExceptionCase Failed(CarriedBy? carriedBy = null, long orderSequence = 7, long shipmentSequence = 12, long number = 5)
    {
        var shipment = OpenExceptions.BookedShipment(carriedBy, orderSequence, shipmentSequence);
        _shipmentsOnFile.Add(shipment);
        return OpenExceptions.DeliveryFailedOn(shipment, number);
    }

    private static TrackingEvent OpenCause(ExceptionCase exception, string sourceEventId)
    {
        var order = Samples.AcceptedOrder(sequence: 9);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 14);
        shipment.Release(order, new IdempotencyKey("k"), new CorrelationId("c"), Samples.Now.AddHours(3),
            CarrierBookingNumber.FromSequence(43));
        shipment.Book();
        return shipment.Record(
            new TrackingReport(
                TrackingSource.Carrier(VanDijk), sourceEventId, "VDT-1", null,
                TrackingEventType.DeliveryFailed, Samples.Now.AddDays(2),
                new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
            new CorrelationId("corr-2"),
            Samples.Now.AddDays(2));
    }
}

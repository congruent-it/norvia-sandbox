using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TrackingEvents;

/// <summary>What is on its way, and what each Carrier actually reported (P5).</summary>
public sealed class TrackingQueriesTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly DateTimeOffset Latest = Samples.Now.AddDays(3);

    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _events = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<ProofOfDelivery> _proofsOfDelivery = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IReadRepository<ShipperNotification> _notifications = Substitute.For<IReadRepository<ShipperNotification>>();
    private readonly IReadRepository<CarrierBooking> _carrierBookings = Substitute.For<IReadRepository<CarrierBooking>>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly List<TransportOrder> _allOrders = [];

    public TrackingQueriesTests()
    {
        _exceptions.ListAsync(Arg.Any<OpenExceptionsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _exceptions.ListAsync(Arg.Any<ExceptionsOfShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _notifications.ListAsync(Arg.Any<ShipperNotificationsOfExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsByShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _proofsOfDelivery.ListAsync(Arg.Any<CurrentProofsOfDeliveryForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _proofsOfDelivery.ListAsync(Arg.Any<ProofsOfDeliveryForShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _orders.ListAsync(Arg.Any<TransportOrdersByNumbersSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allOrders);
    }

    [Fact]
    public async Task ListInProgress_NothingOnItsWay_ListsNothing()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task ListInProgress_ABookedShipmentWithNoEvents_ShowsNoLastEvent_AndTheDeliveryWindowsLatestTimeAsItsEta()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.Should().Be(new ShipmentInProgressSummary(
            "SH-0000012", new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")), ShipmentStatus.Booked, null, Latest, false, false, false));
    }

    [Fact]
    public async Task ListInProgress_ShowsTheLastEventToArrive_AndTheEtaOfTheLastToArriveThatCarriedOne()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var collected = Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1), Samples.Now.AddHours(1), eta: Samples.Now.AddDays(2));
        var departed = Report(shipment, TrackingEventType.Departed, "b", Samples.Now.AddHours(3), Samples.Now.AddHours(3), eta: null);
        var lateOlder = Report(shipment, TrackingEventType.Arrived, "c", Samples.Now.AddHours(2), Samples.Now.AddHours(4), eta: Samples.Now.AddDays(1));
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([collected, departed, lateOlder]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        var summary = listed.Should().ContainSingle().Subject;
        summary.Status.Should().Be(ShipmentStatus.InTransit);
        summary.LastTrackingEvent.Should().Be(new LastTrackingEventView(TrackingEventType.Arrived, Samples.Now.AddHours(2), "Rotterdam", "3011 AA", "NL"));
        summary.Eta.Should().Be(Samples.Now.AddDays(1));
    }

    [Fact]
    public async Task ListInProgress_NotCollectedTheThresholdAfterTheCollectionWindow_IsStale_AndClearsWhenACollectedEventArrives()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var collectionLatest = Samples.Now.AddDays(1).AddHours(4);

        _time.SetUtcNow(collectionLatest.AddHours(2).AddTicks(-1));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();

        _time.SetUtcNow(collectionLatest.AddHours(2));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeTrue();

        var collected = Report(shipment, TrackingEventType.Collected, "a", _time.GetUtcNow(), _time.GetUtcNow(), null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([collected]);
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task ListInProgress_SilentInTransitForTheLimit_IsStale_AndClearsWhenAnotherEventArrives()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var departed = Report(shipment, TrackingEventType.Departed, "a", Samples.Now, Samples.Now, null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([departed]);

        _time.SetUtcNow(Samples.Now.AddHours(24).AddTicks(-1));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();

        _time.SetUtcNow(Samples.Now.AddHours(24));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeTrue();

        var arrived = Report(shipment, TrackingEventType.Arrived, "b", _time.GetUtcNow(), _time.GetUtcNow(), null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([departed, arrived]);
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task ListInProgress_NotDeliveredTheThresholdAfterTheDeliveryWindow_IsStale()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var arrived = Report(shipment, TrackingEventType.Arrived, "a", Latest.AddHours(2), Latest.AddHours(2), null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([arrived]);

        _time.SetUtcNow(Latest.AddHours(2).AddTicks(-1));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();

        _time.SetUtcNow(Latest.AddHours(2));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task ListInProgress_UsesTheAgreedDeliveryWindow_ForStaleAndTheFallbackEta()
    {
        var shipment = Booked(sequence: 12);
        shipment.Record(
            new TrackingReport(TrackingSource.Carrier(VanDijk), "c", "VDT-1", null, TrackingEventType.Collected, Latest.AddHours(-3), new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
            new CorrelationId("corr-c"),
            Latest.AddHours(-3));
        var agreedLatest = Latest.AddDays(2);
        shipment.AgreeDeliveryWindow(new TimeWindow(agreedLatest.AddHours(-4), agreedLatest), ExceptionNumber.FromSequence(5), new IdempotencyKey("key-agree"), new CorrelationId("corr-agree"), Latest.AddHours(-2));
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var departed = Report(shipment, TrackingEventType.Departed, "d", Latest.AddHours(5), Latest.AddHours(5), null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([departed]);

        _time.SetUtcNow(Latest.AddHours(5));
        var summary = (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single();

        summary.IsStale.Should().BeFalse("the Transport Order's window and its grace are past, but the agreed one isn't");
        summary.Eta.Should().Be(agreedLatest);
    }

    [Fact]
    public async Task ListInProgress_ListsStaleFirst_ThenByEta()
    {
        var fresh = Booked(sequence: 12, orderSequence: 7);
        var stale = Booked(sequence: 13, orderSequence: 8);
        var freshSoon = Booked(sequence: 14, orderSequence: 9);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([fresh, stale, freshSoon]);
        var freshEvent = Report(fresh, TrackingEventType.Departed, "a", Samples.Now, Samples.Now, Samples.Now.AddDays(2));
        var freshSoonEvent = Report(freshSoon, TrackingEventType.Departed, "b", Samples.Now, Samples.Now, Samples.Now.AddDays(1));
        var staleEvent = Report(stale, TrackingEventType.Departed, "c", Samples.Now.AddHours(-30), Samples.Now.AddHours(-30), Samples.Now.AddDays(5));
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([freshEvent, freshSoonEvent, staleEvent]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Select(summary => summary.ShipmentNumber).Should().Equal("SH-0000013", "SH-0000014", "SH-0000012");
        listed.Select(summary => summary.IsStale).Should().Equal(true, false, false);
    }

    [Fact]
    public async Task ListInProgress_OnlyEventsOfTheShipmentCountForIt()
    {
        var first = Booked(sequence: 12, orderSequence: 7);
        var second = Booked(sequence: 13, orderSequence: 8);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([first, second]);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([Report(second, TrackingEventType.Collected, "a", Samples.Now, Samples.Now, null)]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Select(summary => summary.LastTrackingEvent is null).Should().Equal(true, false);
    }

    [Fact]
    public async Task ListInProgress_MarksOnlyTheShipmentsWithACurrentProofOfDelivery()
    {
        var first = Booked(sequence: 12, orderSequence: 7);
        var second = Booked(sequence: 13, orderSequence: 8);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([first, second]);
        _proofsOfDelivery.ListAsync(Arg.Any<CurrentProofsOfDeliveryForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([second.Id]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Select(summary => summary.HasProofOfDelivery).Should().Equal(false, true);
    }

    [Fact]
    public async Task Timeline_ListsTheEventsAsTheSpecOrdersThem_WithArrivalAndWhetherEachMovedTheStatus()
    {
        var shipment = Booked(sequence: 12);
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        var moved = Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1), Samples.Now.AddHours(2), Samples.Now.AddDays(2));
        var logged = Report(shipment, TrackingEventType.Collected, "b", Samples.Now.AddHours(2), Samples.Now.AddHours(2), null);
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([moved, logged]);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery("SH-0000012"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TrackingEvents.Should().Equal(
            new TrackingTimelineEntry(
                TrackingEventType.Collected, Samples.Now.AddHours(1), "Rotterdam", "3011 AA", "NL", Samples.Now.AddDays(2), "Collected", Samples.Now.AddHours(2), true),
            new TrackingTimelineEntry(
                TrackingEventType.Collected, Samples.Now.AddHours(2), "Rotterdam", "3011 AA", "NL", null, "Collected", Samples.Now.AddHours(2), false));
    }

    [Fact]
    public async Task Timeline_AlsoListsEachProofOfDelivery_CurrentAndSuperseded_AsTheSpecOrdersThem()
    {
        var shipment = Booked(sequence: 12);
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        var report = new ProofOfDeliveryReport(
            ProofOfDeliverySource.Carrier(VanDijk), "doc-1", "VDT-1", "J. Becker", Samples.Now.AddHours(-3), new TrackingLocation("Rotterdam", "3011 AA", "NL"),
            DeliveryCondition.Clean, null, [new ProofOfDeliveryFile(Guid.NewGuid(), "cmr.pdf", "application/pdf", 4)]);
        var superseded = ProofOfDelivery.Record(shipment, report, new CorrelationId("corr-1"), Samples.Now);
        var current = ProofOfDelivery.Record(shipment, report with { DocumentId = "doc-2", ReceivedBy = "A. Schmidt" }, new CorrelationId("corr-2"), Samples.Now.AddHours(1), superseded);
        _proofsOfDelivery.ListAsync(Arg.Is<ProofsOfDeliveryForShipmentSpec>(spec => spec.ShipmentId == shipment.Id), Arg.Any<CancellationToken>()).Returns([superseded, current]);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery("SH-0000012"), CancellationToken.None);

        result.Value.ProofsOfDelivery.Select(proof => (proof.DocumentId, proof.ReceivedBy, proof.IsCurrent, proof.Files.Count))
            .Should().Equal(("doc-1", "J. Becker", false, 1), ("doc-2", "A. Schmidt", true, 1));
    }

    [Fact]
    public async Task Timeline_AShipmentWithoutEvents_IsEmpty()
    {
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Booked(sequence: 12));
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery("SH-0000012"), CancellationToken.None);

        result.Value.TrackingEvents.Should().BeEmpty();
        result.Value.ProofsOfDelivery.Should().BeEmpty();
        result.Value.Exceptions.Should().BeEmpty();
        result.Value.AgreedDeliveryWindows.Should().BeEmpty();
    }

    [Fact]
    public async Task ListInProgress_MarksTheShipmentsThatHaveAnOpenException()
    {
        var withException = Booked(sequence: 12);
        var without = Booked(sequence: 13, orderSequence: 8);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([withException, without]);
        _exceptions.ListAsync(Arg.Any<OpenExceptionsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([withException.Id]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.ToDictionary(summary => summary.ShipmentNumber, summary => summary.HasOpenException)
            .Should().BeEquivalentTo(new Dictionary<string, bool> { ["SH-0000012"] = true, ["SH-0000013"] = false });
    }

    [Fact]
    public async Task Timeline_ShowsEachExceptionWithItsCausesNotesNotificationsAndOutcome()
    {
        var shipment = Booked(sequence: 12);
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        var failed = ExceptionCase.Open(
            ExceptionNumber.FromSequence(5), shipment,
            ExceptionCause.From(Report(shipment, TrackingEventType.DeliveryFailed, "evt-ndl", Samples.Now.AddDays(1), Samples.Now.AddDays(1), null), Samples.Now.AddDays(1))!);
        failed.AddNote("Phoned Kessler", "Anna de Vries", new CorrelationId("corr-note"), Samples.Now.AddDays(2));
        var notification = ShipperNotification.Request(failed, "REF-1", "Your delivery failed.", "Anna de Vries", null, null, new CorrelationId("corr-notify"), Samples.Now.AddDays(2));
        notification.ReportSent(Samples.Now.AddDays(2).AddMinutes(1));
        failed.Resolve(ExceptionOutcome.Redelivered, "Delivered Thursday", new CorrelationId("corr-resolve"), Samples.Now.AddDays(4));
        var late = ExceptionCase.Open(ExceptionNumber.FromSequence(6), shipment, ExceptionCause.WindowPassed(shipment, new CorrelationId("corr-check"), Samples.Now.AddDays(5)));
        _exceptions.ListAsync(Arg.Is<ExceptionsOfShipmentSpec>(spec => spec.ShipmentId == shipment.Id), Arg.Any<CancellationToken>()).Returns([failed, late]);
        _notifications.ListAsync(Arg.Any<ShipperNotificationsOfExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([notification]);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery("SH-0000012"), CancellationToken.None);

        var exceptions = result.Value.Exceptions;
        exceptions.Select(exception => (exception.ExceptionNumber, exception.Status, exception.Outcome)).Should().Equal(
            ("EX-0000005", ExceptionStatus.Resolved, ExceptionOutcome.Redelivered), ("EX-0000006", ExceptionStatus.Open, (ExceptionOutcome?)null));
        exceptions[0].Causes.Should().ContainSingle().Which.Should().Be(new ExceptionCauseView(ExceptionCauseKind.TrackingEvent, "evt-ndl", Samples.Now.AddDays(1)));
        exceptions[0].Notes.Should().ContainSingle().Which.Should().Be(new ExceptionNoteView("Phoned Kessler", "Anna de Vries", Samples.Now.AddDays(2)));
        exceptions[0].Notifications.Should().ContainSingle().Which.Should().Be(
            new ShipperNotificationView("Your delivery failed.", "Anna de Vries", ShipperNotificationStatus.Sent, Samples.Now.AddDays(2), Samples.Now.AddDays(2).AddMinutes(1), null));
        exceptions[0].Remark.Should().Be("Delivered Thursday");
        exceptions[0].ResolvedAt.Should().Be(Samples.Now.AddDays(4));
        exceptions[1].Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeline_ShowsTheCarrierBookingCancellationOnTheExceptionItWasMadeFrom_AndEachAgreedDeliveryWindow()
    {
        var shipment = Booked(sequence: 12);
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        var late = ExceptionCase.Open(ExceptionNumber.FromSequence(6), shipment, ExceptionCause.WindowPassed(shipment, new CorrelationId("corr-check"), Samples.Now.AddDays(5)));
        _exceptions.ListAsync(Arg.Any<ExceptionsOfShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([late]);
        var booking = CarrierBooking.Request(CarrierBookingNumber.FromSequence(12), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-booking"), Samples.Now);
        booking.Confirm("VDT-1", null, shipment.TransportOrderNumber, new CorrelationId("corr-confirm"), Samples.Now.AddHours(1));
        booking.Cancel("Won't make its collection", late.Number, new IdempotencyKey("key-cancel"), new CorrelationId("corr-cancel"), Samples.Now.AddDays(5));
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsByShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([booking]);
        shipment.Record(
            new TrackingReport(TrackingSource.Carrier(VanDijk), "evt-col", "VDT-1", null, TrackingEventType.Collected, Samples.Now, new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
            new CorrelationId("corr-col"), Samples.Now);
        shipment.AgreeDeliveryWindow(new TimeWindow(Samples.Now.AddDays(6), Samples.Now.AddDays(7)), late.Number, new IdempotencyKey("key-window"), new CorrelationId("corr-window"), Samples.Now.AddDays(5));

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery("SH-0000012"), CancellationToken.None);

        result.Value.Exceptions.Single().CarrierBookingCancellations.Should().ContainSingle().Which.Should().Be(
            new CarrierBookingCancellationView(booking.Number.Value, "Won't make its collection", Samples.Now.AddDays(5), false, null));
        result.Value.AgreedDeliveryWindows.Should().ContainSingle().Which.Should().Be(
            new AgreedDeliveryWindowView(Samples.Now.AddDays(6), Samples.Now.AddDays(7), "EX-0000006", Samples.Now.AddDays(5)));
    }

    [Theory]
    [InlineData("SH-0000099")]
    [InlineData("nonsense")]
    public async Task Timeline_AShipmentTheTmsDoesntHave_IsNotFound(string number)
    {
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((Shipment?)null);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    private ListShipmentsInProgressHandler InProgressHandler() => new(_shipments, _events, _orders, _proofsOfDelivery, _exceptions, _carriers, new ShipmentStaleRules(TimeSpan.FromHours(2), TimeSpan.FromHours(24), TimeSpan.FromHours(2)), _time);

    private GetTrackingTimelineHandler TimelineHandler() => new(_shipments, _events, _proofsOfDelivery, _exceptions, _notifications, _carrierBookings);

    private static TrackingEvent Report(
        Shipment shipment, TrackingEventType type, string id, DateTimeOffset eventTime, DateTimeOffset receivedAt, DateTimeOffset? eta) =>
        shipment.Record(
            new TrackingReport(
                TrackingSource.Carrier(VanDijk), id, "VDT-1", null, type, eventTime, new TrackingLocation("Rotterdam", "3011 AA", "NL"), eta, type.ToString()),
            new CorrelationId("corr"),
            receivedAt);

    /// <summary>A Booked Shipment of a Transport Order whose delivery window ends at <see cref="Latest"/>.</summary>
    private Shipment Booked(long sequence, long orderSequence = 7)
    {
        var details = TransportOrderDetailsSample();
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), details, TransportOrderNumber.FromSequence(orderSequence), IntakeChannel.ShipperApi, new CorrelationId("corr-1"), Samples.Now);
        _allOrders.Add(order);
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.Carrier(VanDijk), new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), Samples.Now.AddHours(2))!;
        shipment.Release(order, new IdempotencyKey("key-rel"), new CorrelationId("corr-rel"), Samples.Now.AddHours(3), CarrierBookingNumber.FromSequence(sequence));
        shipment.Book();
        shipment.ClearDomainEvents();
        return shipment;
    }

    private static TransportOrderDetails TransportOrderDetailsSample()
    {
        var details = Samples.AcceptedOrder().Details;
        return details with { Delivery = details.Delivery! with { Window = new TimeWindow(Latest.AddHours(-4), Latest) } };
    }
}

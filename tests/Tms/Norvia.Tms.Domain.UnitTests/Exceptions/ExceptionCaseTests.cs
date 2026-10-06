using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Exceptions;

/// <summary>
/// A Delivery failed Tracking event opens an Exception for its Shipment (P7), which carries its cause's correlation ID and
/// raises ExceptionOpened. A later cause of the same type joins it, raising nothing. The Planner resolves it with an
/// outcome and a remark, which raises ExceptionResolved; a resolved Exception is final.
/// </summary>
public sealed class ExceptionCaseTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly TrackingLocation Location = new("Rotterdam", "3011 AA", "NL");
    private static readonly TrackingSource Source = TrackingSource.Carrier(new CarrierId("CAR-0001"));

    [Fact]
    public void Open_ForADeliveryFailedEvent_IsOpen_WithTheShipmentsDetails_AndItsCausesCorrelationId()
    {
        var shipment = Booked();
        var cause = DeliveryFailed(shipment, "evt-ndl", "corr-cause");

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(cause));

        exception.Number.Value.Should().Be("EX-0000005");
        exception.Status.Should().Be(ExceptionStatus.Open);
        exception.Type.Should().Be(ExceptionType.DeliveryFailed);
        exception.ShipmentId.Should().Be(shipment.Id);
        exception.ShipmentNumber.Should().Be(shipment.Number);
        exception.TransportOrderNumber.Should().Be(shipment.TransportOrderNumber);
        exception.ShipperId.Should().Be(shipment.ShipperId);
        exception.CarriedBy.Should().Be(shipment.CarriedBy);
        exception.OpenedAt.Should().Be(ReceivedAt);
        exception.CorrelationId.Should().Be(new CorrelationId("corr-cause"));
        exception.AfterDelivered.Should().BeFalse();
        exception.Causes.Should().ContainSingle().Which.SourceEventId.Should().Be("evt-ndl");
    }

    [Fact]
    public void Open_RaisesExceptionOpened_ForTheCause()
    {
        var shipment = Booked();
        var cause = DeliveryFailed(shipment);

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(cause));

        var raised = exception.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>().Subject;
        raised.Exception.Should().BeSameAs(exception);
        raised.Cause.Should().Be(exception.Causes.Single());
        raised.Cause.Kind.Should().Be(ExceptionCauseKind.TrackingEvent);
        raised.Cause.CauseId.Should().Be(cause.Id.Value);
    }

    [Fact]
    public void Open_ForAShipmentAlreadyDelivered_IsMarkedAfterDelivered()
    {
        var shipment = Booked();
        shipment.Record(new TrackingReport(Source, "evt-del", "VDT-1", null, TrackingEventType.Delivered, ReceivedAt, Location, null, null), new CorrelationId("c"), ReceivedAt);

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));

        exception.AfterDelivered.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
    }

    [Theory]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Arrived)]
    [InlineData(TrackingEventType.OutForDelivery)]
    [InlineData(TrackingEventType.Delivered)]
    public void TypeOf_AProgressTrackingEvent_IsNone(TrackingEventType type)
    {
        ExceptionTypes.OpenedBy(type).Should().BeNull();
    }

    [Theory]
    [InlineData(TrackingEventType.CollectionFailed, ExceptionType.CollectionFailed)]
    [InlineData(TrackingEventType.DeliveryFailed, ExceptionType.DeliveryFailed)]
    [InlineData(TrackingEventType.Damaged, ExceptionType.Damaged)]
    public void TypeOf_AnExceptionTrackingEvent_IsItsOwnExceptionType(TrackingEventType type, ExceptionType expected)
    {
        ExceptionTypes.OpenedBy(type).Should().Be(expected);
    }

    [Fact]
    public void TypeOf_AProofOfDelivery_IsReservations_OnlyWithReservations()
    {
        ExceptionTypes.OpenedBy(DeliveryCondition.Reservations).Should().Be(ExceptionType.Reservations);
        ExceptionTypes.OpenedBy(DeliveryCondition.Clean).Should().BeNull();
    }

    [Theory]
    [InlineData(TrackingEventType.CollectionFailed, ExceptionType.CollectionFailed)]
    [InlineData(TrackingEventType.Damaged, ExceptionType.Damaged)]
    public void Open_ForACollectionFailedOrDamagedEvent_OpensAnExceptionOfThatType(TrackingEventType type, ExceptionType expected)
    {
        var shipment = Booked();
        var cause = Report(shipment, type, "evt-x", "corr-x");

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(cause));

        exception.Type.Should().Be(expected);
        exception.Causes.Should().ContainSingle().Which.CauseId.Should().Be(cause.Id.Value);
        exception.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>();
    }

    [Fact]
    public void Open_ForAProofOfDeliveryWithReservations_OpensAReservationsException_WithItsCorrelationId()
    {
        var shipment = Booked();
        var cause = ProofWith(shipment, DeliveryCondition.Reservations, "doc-1", "corr-pod");

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(cause));

        exception.Type.Should().Be(ExceptionType.Reservations);
        exception.CorrelationId.Should().Be(new CorrelationId("corr-pod"));
        exception.AfterDelivered.Should().BeFalse();
        var only = exception.Causes.Should().ContainSingle().Which;
        only.Kind.Should().Be(ExceptionCauseKind.ProofOfDelivery);
        only.CauseId.Should().Be(cause.Id.Value);
        only.SourceEventId.Should().Be("doc-1");
        exception.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>().Which.Cause.Should().Be(only);
    }

    [Fact]
    public void Open_ForAProofOfDeliveryOfADeliveredShipment_IsNotMarkedAfterDelivered()
    {
        var shipment = Booked();
        shipment.Record(new TrackingReport(Source, "evt-del", "VDT-1", null, TrackingEventType.Delivered, ReceivedAt, Location, null, null), new CorrelationId("c"), ReceivedAt);

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(ProofWith(shipment, DeliveryCondition.Reservations)));

        exception.AfterDelivered.Should().BeFalse("the marker is for Exceptions opened by Tracking events; a Proof of delivery usually comes after Delivered");
    }

    [Fact]
    public void Open_ForACleanProofOfDelivery_Throws()
    {
        var shipment = Booked();

        var open = () => ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(ProofWith(shipment, DeliveryCondition.Clean)));

        open.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddCause_ForAnotherProofOfDeliveryWithReservations_JoinsTheOpenException_AndRaisesNothing()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(ProofWith(shipment, DeliveryCondition.Reservations, "doc-1")));
        exception.ClearDomainEvents();

        exception.AddCause(Cause(ProofWith(shipment, DeliveryCondition.Reservations, "doc-2"), ReceivedAt.AddHours(1)));

        exception.Causes.Select(cause => cause.SourceEventId).Should().Equal("doc-1", "doc-2");
        exception.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AddCause_OfAnotherType_Throws()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));

        var addTrackingEvent = () => exception.AddCause(Cause(Report(shipment, TrackingEventType.Damaged, "evt-dmg", "c")));
        var addProof = () => exception.AddCause(Cause(ProofWith(shipment, DeliveryCondition.Reservations)));

        addTrackingEvent.Should().Throw<ArgumentException>();
        addProof.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_ACauseThatOpensNoException_IsNone()
    {
        var shipment = Booked();

        ExceptionCause.From(Report(shipment, TrackingEventType.Collected, "evt-col", "c"), ReceivedAt).Should().BeNull();
        ExceptionCause.From(ProofWith(shipment, DeliveryCondition.Clean), ReceivedAt).Should().BeNull();
    }

    [Fact]
    public void AddCause_ForAnotherDeliveryFailed_JoinsTheOpenException_AndRaisesNothing()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));
        exception.ClearDomainEvents();

        exception.AddCause(Cause(DeliveryFailed(shipment, "evt-2", "corr-2"), ReceivedAt.AddHours(1)));

        exception.Causes.Select(cause => cause.SourceEventId).Should().Equal("evt-ndl", "evt-2");
        exception.CorrelationId.Should().Be(new CorrelationId("corr-cause"), "the Exception keeps the correlation ID of the cause that opened it");
        exception.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_AnOpenException_RecordsOutcomeAndRemark_AndRaisesExceptionResolved()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));
        exception.ClearDomainEvents();

        var refusal = exception.Resolve(ExceptionOutcome.Redelivered, "Phoned Kessler, redelivery Thursday", new CorrelationId("corr-resolve"), ReceivedAt.AddDays(1));

        refusal.Should().BeNull();
        exception.Status.Should().Be(ExceptionStatus.Resolved);
        exception.Outcome.Should().Be(ExceptionOutcome.Redelivered);
        exception.Remark.Should().Be("Phoned Kessler, redelivery Thursday");
        exception.ResolvedAt.Should().Be(ReceivedAt.AddDays(1));
        exception.ResolutionCorrelationId.Should().Be(new CorrelationId("corr-resolve"));
        exception.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionResolvedEvent>().Which.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public void AddNote_ToAnOpenException_RecordsItWithThePlannerTimeAndCorrelationId_AndRaisesNothing()
    {
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), Booked(), Cause(DeliveryFailed(Booked())));
        exception.ClearDomainEvents();

        var refusal = exception.AddNote("Phoned Kessler, redelivery Thursday", "Anna de Vries", new CorrelationId("corr-note"), ReceivedAt.AddHours(2));

        refusal.Should().BeNull();
        exception.Notes.Should().ContainSingle().Which.Should().Be(
            new ExceptionNote("Phoned Kessler, redelivery Thursday", "Anna de Vries", new CorrelationId("corr-note"), ReceivedAt.AddHours(2)));
        exception.Status.Should().Be(ExceptionStatus.Open);
        exception.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AddNote_Twice_KeepsBothInTheOrderTheyCame()
    {
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), Booked(), Cause(DeliveryFailed(Booked())));

        exception.AddNote("First", "Anna", new CorrelationId("c1"), ReceivedAt.AddHours(1));
        exception.AddNote("Second", "Anna", new CorrelationId("c2"), ReceivedAt.AddHours(2));

        exception.Notes.Select(note => note.Text).Should().Equal("First", "Second");
    }

    [Fact]
    public void AddNote_ToAResolvedException_IsRefused_WithExceptionNotOpen_AndAddsNothing()
    {
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), Booked(), Cause(DeliveryFailed(Booked())));
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Done", new CorrelationId("c"), ReceivedAt);

        var refusal = exception.AddNote("Too late", "Anna", new CorrelationId("c2"), ReceivedAt.AddDays(1));

        refusal.Should().NotBeNull();
        refusal!.Code.Should().Be(ExceptionRefusalCode.ExceptionNotOpen);
        exception.Notes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AddNote_WithoutText_Throws_BecauseTheBoundaryChecksItFirst(string? text)
    {
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), Booked(), Cause(DeliveryFailed(Booked())));

        var add = () => exception.AddNote(text!, "Anna", new CorrelationId("c"), ReceivedAt);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_AResolvedException_IsRefused_WithExceptionNotOpen_AndChangesNothing()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "First", new CorrelationId("corr-1"), ReceivedAt);
        exception.ClearDomainEvents();

        var refusal = exception.Resolve(ExceptionOutcome.Replanned, "Second", new CorrelationId("corr-2"), ReceivedAt.AddDays(1));

        refusal.Should().NotBeNull().And.Match<ExceptionRefusal>(r => r.Code == ExceptionRefusalCode.ExceptionNotOpen);
        exception.Outcome.Should().Be(ExceptionOutcome.NoActionNeeded);
        exception.Remark.Should().Be("First");
        exception.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithoutARemark_Throws_BecauseTheBoundaryChecksItFirst(string remark)
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));

        var resolve = () => exception.Resolve(ExceptionOutcome.NoActionNeeded, remark, new CorrelationId("c"), ReceivedAt);

        resolve.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_WithAnOutcomeOutsideTheList_Throws()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));

        var resolve = () => exception.Resolve((ExceptionOutcome)99, "remark", new CorrelationId("c"), ReceivedAt);

        resolve.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkCauseCleared_MarksTheCause_KeepsTheExceptionOpen_AndRaisesNothing()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));
        exception.ClearDomainEvents();

        exception.MarkCauseCleared();

        exception.CauseCleared.Should().BeTrue();
        exception.Status.Should().Be(ExceptionStatus.Open);
        exception.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkCauseCleared_ForADamagedException_NeverMarksIt()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(Report(shipment, TrackingEventType.Damaged, "evt-dmg", "corr-dmg")));

        exception.MarkCauseCleared();

        exception.CauseCleared.Should().BeFalse();
    }

    [Fact]
    public void MarkCauseCleared_ForAResolvedException_DoesNotMarkIt()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Checked", new CorrelationId("c"), ReceivedAt);

        exception.MarkCauseCleared();

        exception.CauseCleared.Should().BeFalse();
    }

    [Fact]
    public void AddCause_RemovesTheCauseClearedMarker()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, Cause(DeliveryFailed(shipment)));
        exception.MarkCauseCleared();

        exception.AddCause(Cause(DeliveryFailed(shipment, "evt-ndl-2", "corr-2")));

        exception.CauseCleared.Should().BeFalse();
    }

    [Fact]
    public void ClearedBy_Collected_IsCollectionFailed() =>
        ExceptionTypes.ClearedBy(TrackingEventType.Collected).Should().Equal(ExceptionType.CollectionFailed);

    [Fact]
    public void ClearedBy_Delivered_IsDeliveryFailedAndLateDelivery() =>
        ExceptionTypes.ClearedBy(TrackingEventType.Delivered).Should().BeEquivalentTo([ExceptionType.DeliveryFailed, ExceptionType.LateDelivery]);

    [Theory]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Damaged)]
    [InlineData(TrackingEventType.DeliveryFailed)]
    public void ClearedBy_AnyOtherTrackingEvent_IsNone(TrackingEventType type) =>
        ExceptionTypes.ClearedBy(type).Should().BeEmpty();

    [Fact]
    public void ClearedBy_ACleanProofOfDelivery_IsReservations_AndOneWithReservationsIsNone()
    {
        ExceptionTypes.ClearedBy(DeliveryCondition.Clean).Should().Be(ExceptionType.Reservations);
        ExceptionTypes.ClearedBy(DeliveryCondition.Reservations).Should().BeNull();
    }

    [Fact]
    public void ExceptionNumber_IsAnExPrefixAndSevenDigits_AndParses()
    {
        ExceptionNumber.FromSequence(42).Value.Should().Be("EX-0000042");
        ExceptionNumber.TryParse("EX-0000042", out var number).Should().BeTrue();
        number!.Value.Should().Be("EX-0000042");
        ExceptionNumber.TryParse("SH-0000042", out _).Should().BeFalse();
        ExceptionNumber.TryParse(null, out _).Should().BeFalse();
        var parse = () => ExceptionNumber.Parse("nope");
        parse.Should().Throw<FormatException>();
    }

    private static ExceptionCause Cause(TrackingEvent trackingEvent, DateTimeOffset? at = null) => ExceptionCause.From(trackingEvent, at ?? ReceivedAt)!;

    private static ExceptionCause Cause(ProofOfDelivery proofOfDelivery, DateTimeOffset? at = null) => ExceptionCause.From(proofOfDelivery, at ?? ReceivedAt)!;

    private static TrackingEvent DeliveryFailed(Shipment shipment, string sourceEventId = "evt-ndl", string correlationId = "corr-cause") =>
        Report(shipment, TrackingEventType.DeliveryFailed, sourceEventId, correlationId);

    private static TrackingEvent Report(Shipment shipment, TrackingEventType type, string sourceEventId, string correlationId) =>
        shipment.Record(
            new TrackingReport(Source, sourceEventId, "VDT-1", null, type, ReceivedAt, Location, null, "Consignee closed"),
            new CorrelationId(correlationId),
            ReceivedAt);

    private static ProofOfDelivery ProofWith(Shipment shipment, DeliveryCondition condition, string documentId = "doc-1", string correlationId = "corr-pod") =>
        ProofOfDelivery.Record(
            shipment,
            new ProofOfDeliveryReport(
                ProofOfDeliverySource.Carrier(new CarrierId("CAR-0001")),
                documentId,
                "VDT-1",
                "J. Becker",
                ReceivedAt,
                Location,
                condition,
                "Two pallets short",
                [new ProofOfDeliveryFile(Guid.NewGuid(), "cmr.pdf", "application/pdf", 4)]),
            new CorrelationId(correlationId),
            ReceivedAt);

    private static Shipment Booked()
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);
        var shipment = order.Plan(
            ShipmentNumber.FromSequence(3),
            CarriedBy.Carrier(new CarrierId("CAR-0001")),
            new IdempotencyKey("key-1"),
            new CorrelationId("corr-planning"),
            ReceivedAt.AddHours(2))!;
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), ReceivedAt.AddHours(3), CarrierBookingNumber.FromSequence(42));
        shipment.Book();
        shipment.ClearDomainEvents();
        return shipment;
    }
}

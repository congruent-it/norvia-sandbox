using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
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

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, cause, ReceivedAt);

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

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, cause, ReceivedAt);

        var raised = exception.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>().Subject;
        raised.Exception.Should().BeSameAs(exception);
        raised.Cause.Should().BeSameAs(cause);
    }

    [Fact]
    public void Open_ForAShipmentAlreadyDelivered_IsMarkedAfterDelivered()
    {
        var shipment = Booked();
        shipment.Record(new TrackingReport(Source, "evt-del", "VDT-1", null, TrackingEventType.Delivered, ReceivedAt, Location, null, null), new CorrelationId("c"), ReceivedAt);

        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, DeliveryFailed(shipment), ReceivedAt);

        exception.AfterDelivered.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
    }

    [Theory]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.CollectionFailed)]
    [InlineData(TrackingEventType.Damaged)]
    public void TypeOf_ATrackingEventThatIsNotDeliveryFailed_IsNone_UntilItsExceptionTypeIsBuilt(TrackingEventType type)
    {
        ExceptionTypes.OpenedBy(type).Should().BeNull();
    }

    [Fact]
    public void TypeOf_DeliveryFailed_IsDeliveryFailed()
    {
        ExceptionTypes.OpenedBy(TrackingEventType.DeliveryFailed).Should().Be(ExceptionType.DeliveryFailed);
    }

    [Fact]
    public void AddCause_ForAnotherDeliveryFailed_JoinsTheOpenException_AndRaisesNothing()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, DeliveryFailed(shipment), ReceivedAt);
        exception.ClearDomainEvents();

        exception.AddCause(DeliveryFailed(shipment, "evt-2", "corr-2"), ReceivedAt.AddHours(1));

        exception.Causes.Select(cause => cause.SourceEventId).Should().Equal("evt-ndl", "evt-2");
        exception.CorrelationId.Should().Be(new CorrelationId("corr-cause"), "the Exception keeps the correlation ID of the cause that opened it");
        exception.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_AnOpenException_RecordsOutcomeAndRemark_AndRaisesExceptionResolved()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, DeliveryFailed(shipment), ReceivedAt);
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
    public void Resolve_AResolvedException_IsRefused_WithExceptionNotOpen_AndChangesNothing()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, DeliveryFailed(shipment), ReceivedAt);
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
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, DeliveryFailed(shipment), ReceivedAt);

        var resolve = () => exception.Resolve(ExceptionOutcome.NoActionNeeded, remark, new CorrelationId("c"), ReceivedAt);

        resolve.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_WithAnOutcomeOutsideTheList_Throws()
    {
        var shipment = Booked();
        var exception = ExceptionCase.Open(ExceptionNumber.FromSequence(5), shipment, DeliveryFailed(shipment), ReceivedAt);

        var resolve = () => exception.Resolve((ExceptionOutcome)99, "remark", new CorrelationId("c"), ReceivedAt);

        resolve.Should().Throw<ArgumentOutOfRangeException>();
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

    private static TrackingEvent DeliveryFailed(Shipment shipment, string sourceEventId = "evt-ndl", string correlationId = "corr-cause") =>
        shipment.Record(
            new TrackingReport(Source, sourceEventId, "VDT-1", null, TrackingEventType.DeliveryFailed, ReceivedAt, Location, null, "Consignee closed"),
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

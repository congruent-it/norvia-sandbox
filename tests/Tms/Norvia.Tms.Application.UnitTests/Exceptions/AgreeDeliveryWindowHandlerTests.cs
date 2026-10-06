using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

/// <summary>From an open Exception the Planner agrees a delivery window on a Collected or In transit Shipment (P7).</summary>
public sealed class AgreeDeliveryWindowHandlerTests
{
    private static readonly DateTimeOffset OrderLatest = Samples.Now.AddDays(1).AddHours(10);
    private static readonly DateTimeOffset Earliest = Samples.Now.AddDays(4);
    private static readonly DateTimeOffset Latest = Samples.Now.AddDays(4).AddHours(4);

    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IExceptionNumbers _exceptionNumbers = Substitute.For<IExceptionNumbers>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(3));
    private readonly AgreeDeliveryWindowHandler _handler;

    public AgreeDeliveryWindowHandlerTests()
    {
        _trackingEvents.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new AgreeDeliveryWindowHandler(_exceptions, _shipments, _trackingEvents, new ExceptionCauses(_exceptions, _exceptionNumbers), _time);
    }

    [Fact]
    public async Task Handle_OnACollectedShipment_KeepsTheWindow_RaisesShipmentDeliveryWindowAgreed_AndSavesOnce()
    {
        var (exception, shipment) = Found(OpenExceptions.DeliveryFailed());

        var result = await _handler.Handle(Agree(), CancellationToken.None);

        result.Value.Should().Be(new AgreeDeliveryWindowResult("EX-0000005", "SH-0000012", Earliest, Latest, "corr-agree"));
        shipment.AgreedDeliveryWindows.Should().ContainSingle().Which.Should().Be(
            new AgreedDeliveryWindow(Earliest, Latest, ExceptionNumber.FromSequence(5), new CorrelationId("corr-agree"), Samples.Now.AddDays(3), new IdempotencyKey("key-agree")));
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentDeliveryWindowAgreedEvent>();
        exception.Status.Should().Be(ExceptionStatus.Open);
        await _exceptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnOpenLateDeliveryWhoseEtaIsInsideTheNewWindow_IsMarkedCauseCleared()
    {
        var (_, shipment) = Found(OpenExceptions.DeliveryFailed());
        var late = OpenExceptions.LateDelivery();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.LateDelivery), Arg.Any<CancellationToken>())
            .Returns(late);
        _trackingEvents.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([EtaEvent(shipment, OrderLatest.AddDays(1))]);

        await _handler.Handle(Agree(), CancellationToken.None);

        late.CauseCleared.Should().BeTrue();
        late.Status.Should().Be(ExceptionStatus.Open);
    }

    [Fact]
    public async Task Handle_AnOpenLateDeliveryWhoseEtaIsStillAfterTheNewWindow_StaysUncleared()
    {
        var (_, shipment) = Found(OpenExceptions.DeliveryFailed());
        var late = OpenExceptions.LateDelivery();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.LateDelivery), Arg.Any<CancellationToken>())
            .Returns(late);
        _trackingEvents.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([EtaEvent(shipment, Latest.AddMinutes(1))]);

        await _handler.Handle(Agree(), CancellationToken.None);

        late.CauseCleared.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TheSameAgreementSentAgain_IsARepeat_AnsweredAsBefore_WithoutAgreeingPublishingOrSavingAgain()
    {
        var (_, shipment) = Found(OpenExceptions.DeliveryFailed());
        await _handler.Handle(Agree(), CancellationToken.None);
        shipment.ClearDomainEvents();
        _exceptions.ClearReceivedCalls();

        var result = await _handler.Handle(Agree() with { CorrelationId = "corr-retry" }, CancellationToken.None);

        result.Value.Should().Be(new AgreeDeliveryWindowResult("EX-0000005", "SH-0000012", Earliest, Latest, "corr-agree", Repeat: true));
        shipment.AgreedDeliveryWindows.Should().ContainSingle();
        shipment.DomainEvents.Should().BeEmpty();
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARepeatAfterTheExceptionWasResolved_IsStillAnsweredAsBefore()
    {
        var exception = OpenExceptions.DeliveryFailed();
        Found(exception);
        await _handler.Handle(Agree(), CancellationToken.None);
        exception.Resolve(ExceptionOutcome.Redelivered, "Done", new CorrelationId("corr-res"), Samples.Now.AddDays(3));

        var result = await _handler.Handle(Agree(), CancellationToken.None);

        result.Value.Repeat.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AnotherKey_AgreesAgain_AndKeepsTheEarlierWindow()
    {
        var (_, shipment) = Found(OpenExceptions.DeliveryFailed());
        await _handler.Handle(Agree(), CancellationToken.None);

        await _handler.Handle(Agree() with { AgreementKey = "key-later", Latest = Latest.AddDays(1) }, CancellationToken.None);

        shipment.AgreedDeliveryWindows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_OnABookedShipment_IsRefused_WithShipmentNotInTransport()
    {
        Found(OpenExceptions.DeliveryFailed(), collected: false);

        var result = await _handler.Handle(Agree(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ShipmentNotInTransport);
    }

    [Fact]
    public async Task Handle_AWindowEndingInThePast_IsRefused_WithDeliveryWindowInvalid()
    {
        var (_, shipment) = Found(OpenExceptions.DeliveryFailed());

        var result = await _handler.Handle(Agree() with { Latest = Samples.Now.AddDays(3).AddMinutes(-1) }, CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.DeliveryWindowInvalid);
        shipment.AgreedDeliveryWindows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("EX-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_AnUnknownException_IsRefused_WithExceptionNotFound(string number)
    {
        var result = await _handler.Handle(Agree(number), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotFound);
    }

    [Fact]
    public async Task Handle_AResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Done", new CorrelationId("corr-1"), Samples.Now.AddDays(2));
        var (_, shipment) = Found(exception);

        var result = await _handler.Handle(Agree(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotOpen);
        shipment.AgreedDeliveryWindows.Should().BeEmpty();
    }

    private async Task AssertRefused(Result<AgreeDeliveryWindowResult> result, ExceptionRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private (ExceptionCase Exception, Shipment Shipment) Found(ExceptionCase exception, bool collected = true)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(3), CarrierBookingNumber.FromSequence(42));
        shipment.Book();
        if (collected)
        {
            shipment.Record(
                new TrackingReport(TrackingSource.Carrier(new CarrierId("CAR-0001")), "evt-1", "VDT-1", null, TrackingEventType.Collected, Samples.Now.AddDays(1), new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
                new CorrelationId("corr-tracking"),
                Samples.Now.AddDays(1));
        }

        shipment.ClearDomainEvents();
        _exceptions.SingleOrDefaultAsync(Arg.Is<ExceptionToResolveSpec>(s => s.Number == exception.Number), Arg.Any<CancellationToken>()).Returns(exception);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(s => s.Number == exception.ShipmentNumber), Arg.Any<CancellationToken>()).Returns(shipment);
        return (exception, shipment);
    }

    private static TrackingEvent EtaEvent(Shipment shipment, DateTimeOffset eta) =>
        shipment.Record(
            new TrackingReport(TrackingSource.Carrier(new CarrierId("CAR-0001")), "evt-eta", "VDT-1", null, TrackingEventType.Departed, Samples.Now.AddDays(2), new TrackingLocation("Rotterdam", "3011 AA", "NL"), eta, null),
            new CorrelationId("corr-eta"),
            Samples.Now.AddDays(2));

    private static AgreeDeliveryWindowCommand Agree(string number = "EX-0000005") => new(number, Earliest, Latest, "key-agree", "corr-agree");
}

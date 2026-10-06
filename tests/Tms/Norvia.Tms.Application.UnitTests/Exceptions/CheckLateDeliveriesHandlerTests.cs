using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.CheckLate;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

/// <summary>The time-based check opens a Late delivery Exception when the delivery window plus the grace has passed without Delivered (P7).</summary>
public sealed class CheckLateDeliveriesHandlerTests
{
    private static readonly DateTimeOffset WindowLatest = Samples.Now.AddDays(1).AddHours(10);

    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TransportOrder> _transportOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IExceptionNumbers _exceptionNumbers = Substitute.For<IExceptionNumbers>();
    private readonly FakeTimeProvider _time = new(WindowLatest.AddMinutes(-1));

    public CheckLateDeliveriesHandlerTests() =>
        _exceptionNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(ExceptionNumber.FromSequence(9));

    [Fact]
    public async Task Handle_BeforeTheWindowsLatestTime_OpensNothing()
    {
        OnItsWay();

        await Handler(TimeSpan.Zero).Handle(new CheckLateDeliveriesCommand(), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheWindowHasPassed_OpensALateDeliveryException_WithANewCorrelationId_AndSavesIt()
    {
        var shipment = OnItsWay();
        _time.Advance(TimeSpan.FromMinutes(1));

        await Handler(TimeSpan.Zero).Handle(new CheckLateDeliveriesCommand(), CancellationToken.None);

        var opened = (ExceptionCase)_exceptions.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ExceptionCase>.AddAsync)).GetArguments()[0]!;
        opened.Type.Should().Be(ExceptionType.LateDelivery);
        opened.ShipmentId.Should().Be(shipment.Id);
        opened.OpenedAt.Should().Be(WindowLatest);
        opened.CorrelationId.Value.Should().NotBeNullOrWhiteSpace();
        opened.Causes.Should().ContainSingle().Which.Should().Match<ExceptionCause>(cause => cause.Kind == ExceptionCauseKind.WindowPassed && cause.SourceEventId == null);
        opened.AfterDelivered.Should().BeFalse();
        opened.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>();
        await _exceptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheGraceIsHonoured()
    {
        OnItsWay();
        _time.Advance(TimeSpan.FromMinutes(90));
        var handler = Handler(TimeSpan.FromHours(2));

        await handler.Handle(new CheckLateDeliveriesCommand(), CancellationToken.None);
        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromMinutes(31));
        await handler.Handle(new CheckLateDeliveriesCommand(), CancellationToken.None);
        await _exceptions.Received(1).AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenALateDeliveryExceptionIsOpenAlready_AddsNoCause_AndOpensNothing()
    {
        var shipment = OnItsWay();
        _time.Advance(TimeSpan.FromHours(1));
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.LateDelivery), Arg.Any<CancellationToken>())
            .Returns(open);

        await Handler(TimeSpan.Zero).Handle(new CheckLateDeliveriesCommand(), CancellationToken.None);

        open.Causes.Should().ContainSingle();
        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheOpenLateDeliveryCauseWasCleared_AddsTheWindowPassedCause_ToRemoveTheMarker()
    {
        var shipment = OnItsWay();
        _time.Advance(TimeSpan.FromHours(1));
        var open = OpenExceptions.LateDelivery();
        open.MarkCauseCleared();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.LateDelivery), Arg.Any<CancellationToken>())
            .Returns(open);

        await Handler(TimeSpan.Zero).Handle(new CheckLateDeliveriesCommand(), CancellationToken.None);

        open.Causes.Should().HaveCount(2);
        open.CauseCleared.Should().BeFalse();
        open.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNoShipmentsOnTheirWay_DoesNothing()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        await Handler(TimeSpan.Zero).Handle(new CheckLateDeliveriesCommand(), CancellationToken.None);

        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private CheckLateDeliveriesHandler Handler(TimeSpan grace) =>
        new(_shipments, _transportOrders, _exceptions, new ExceptionCauses(_exceptions, _exceptionNumbers), new LateDeliveryRules(grace), _time);

    private Shipment OnItsWay()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        _transportOrders.ListAsync(Arg.Any<TransportOrdersByNumbersSpec>(), Arg.Any<CancellationToken>()).Returns([order]);
        return shipment;
    }
}

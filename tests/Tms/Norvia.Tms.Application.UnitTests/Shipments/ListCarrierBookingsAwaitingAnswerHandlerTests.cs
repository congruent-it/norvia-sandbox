using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

/// <summary>P4's third Planner list: the Requested Carrier bookings awaiting an answer, sorted by earliest collection time.</summary>
public sealed class ListCarrierBookingsAwaitingAnswerHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IReadRepository<CarrierBooking> _carrierBookings = Substitute.For<IReadRepository<CarrierBooking>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TransportOrder> _transportOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly CarrierBookingOverdueRules _rules = new(TimeSpan.FromHours(2));
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(1).AddMinutes(30));
    private readonly List<CarrierBooking> _allBookings = [];
    private readonly List<Shipment> _allShipments = [];
    private readonly List<TransportOrder> _allOrders = [];
    private readonly ListCarrierBookingsAwaitingAnswerHandler _handler;

    public ListCarrierBookingsAwaitingAnswerHandlerTests()
    {
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _carrierBookings.ListAsync(Arg.Any<RequestedCarrierBookingsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allBookings);
        _shipments.ListAsync(Arg.Any<ShipmentsByIdsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allShipments);
        _transportOrders.ListAsync(Arg.Any<TransportOrdersByNumbersSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allOrders);
        _handler = new ListCarrierBookingsAwaitingAnswerHandler(_carrierBookings, _shipments, _transportOrders, _carriers, _rules, _time);
    }

    [Fact]
    public async Task Handle_NoRequestedBookings_ListsNothing()
    {
        _carrierBookings.ListAsync(Arg.Any<RequestedCarrierBookingsSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        listed.Should().BeEmpty();
        await _shipments.DidNotReceive().ListAsync(Arg.Any<ShipmentsByIdsSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARequestedBookingNotYetSent_ListsIt_WithNoSentTime()
    {
        var (booking, _) = Requested(sequence: 7, shipmentSequence: 12);

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        var summary = listed.Should().ContainSingle().Subject;
        summary.ShipmentNumber.Should().Be("SH-0000012");
        summary.CarrierBookingNumber.Should().Be(booking.Number.Value);
        summary.Carrier.Should().Be(new CarrierView("CAR-0001", "Van Dijk Transport"));
        summary.RequestedAt.Should().Be(booking.RequestedAt);
        summary.SentAt.Should().BeNull();
        summary.EarliestCollectionTime.Should().Be(Samples.Now.AddDays(1), "the sample Transport Order's own collection window");
    }

    [Fact]
    public async Task Handle_ABookingWithAFrozenCarrierCost_ListsItWithItsSource()
    {
        var cost = new CarrierCost(
            [new ChargeLine(ChargeLineType.Freight, new Money(86.50m))],
            CarrierCostSource.CarrierRateCard(new RateCardVersionReference(new RateCardId("CRC-0001"), 2)),
            Samples.Now.AddHours(1));
        Requested(sequence: 7, shipmentSequence: 12, carrierCost: cost);

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.CarrierCost.Should().Be(new CarrierCostView(
            "EUR",
            86.50m,
            [new ChargeLineView("Freight", 86.50m)],
            new CarrierCostSourceView("CarrierRateCard", new RateCardVersionView("CRC-0001", 2)),
            Samples.Now.AddHours(1)));
    }

    [Fact]
    public async Task Handle_ARequestedBookingMarkedSent_ListsItsSentTime()
    {
        var (booking, _) = Requested(sequence: 7, shipmentSequence: 12);
        booking.MarkSent(Samples.Now.AddHours(1));

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.SentAt.Should().Be(Samples.Now.AddHours(1));
    }

    [Fact]
    public async Task Handle_ARequestedForLongerThanTheOverdueSetting_IsFlaggedOverdue()
    {
        Requested(sequence: 7, shipmentSequence: 12);
        _time.SetUtcNow(Samples.Now.AddHours(1).Add(TimeSpan.FromHours(2)));

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.Overdue.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RequestedRecentlyWithACollectionWindowThatHasPassed_IsFlaggedOverdue()
    {
        Requested(sequence: 7, shipmentSequence: 12);
        _time.SetUtcNow(Samples.Now.AddDays(1).AddMinutes(1));

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.Overdue.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RequestedRecentlyWithNeitherPassed_IsNotFlaggedOverdue()
    {
        Requested(sequence: 7, shipmentSequence: 12);

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.Overdue.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SeveralRequestedBookings_ListsThemSortedByEarliestCollectionTime()
    {
        var (early, _) = Requested(sequence: 7, shipmentSequence: 12, collectionWindowOffsetDays: 1);
        var (late, _) = Requested(sequence: 8, shipmentSequence: 13, collectionWindowOffsetDays: 3);

        var listed = await _handler.Handle(new ListCarrierBookingsAwaitingAnswerQuery(), CancellationToken.None);

        listed.Select(summary => summary.CarrierBookingNumber).Should().Equal(early.Number.Value, late.Number.Value);
    }

    private (CarrierBooking Booking, Shipment Shipment) Requested(long sequence, long shipmentSequence, int collectionWindowOffsetDays = 1, CarrierCost? carrierCost = null)
    {
        var order = Samples.AcceptedOrder(sequence);
        if (collectionWindowOffsetDays != 1)
        {
            order = TransportOrder.Accept(
                order.ShipperId,
                order.Details with { Collection = order.Details.Collection! with { Window = new(Samples.Now.AddDays(collectionWindowOffsetDays), Samples.Now.AddDays(collectionWindowOffsetDays).AddHours(4)) } },
                order.Number!,
                IntakeChannel.ShipperApi,
                order.CorrelationId,
                Samples.Now);
        }

        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), shipmentSequence);
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(sequence), shipment.Id, shipment.Number, VanDijk, new CorrelationId($"corr-release-{sequence}"), Samples.Now.AddHours(1), carrierCost);

        _allBookings.Add(booking);
        _allShipments.Add(shipment);
        _allOrders.Add(order);

        return (booking, shipment);
    }
}

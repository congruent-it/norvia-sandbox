using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class ShipmentsToReleaseQueryTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IReadRepository<Shipment> _repository = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<CarrierBooking> _carrierBookings = Substitute.For<IReadRepository<CarrierBooking>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly IReadRepository<TransportOrder> _transportOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly ICarrierRateCardDirectory _carrierRateCards = Substitute.For<ICarrierRateCardDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly ListShipmentsToReleaseHandler _handler;

    public ShipmentsToReleaseQueryTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _transportOrders.ListAsync(Arg.Any<TransportOrdersByNumbersSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard());
        _handler = new ListShipmentsToReleaseHandler(_repository, _carrierBookings, _transportOrders, _shippers, _carriers, _carrierRateCards, _time);
    }

    [Fact]
    public async Task Handle_ACarrierShipmentItsCarrierRateCardCantPrice_ShowsThePricingFailureNamingTheCarrier()
    {
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard(laneDestination: "Groningen"));
        var (_, shipment) = PlannedWithOrder(CarriedBy.Carrier(VanDijk));

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        var failure = shipments.Should().ContainSingle().Which.PricingFailure;
        failure!.Code.Should().Be("NoMatchingLane");
        failure.Message.Should().Contain("Van Dijk Transport");
        shipments[0].ShipmentNumber.Should().Be(shipment.Number.Value);
    }

    [Fact]
    public async Task Handle_ACarrierWithoutACarrierRateCard_ShowsNoRateCard()
    {
        _carrierRateCards.FindFor(VanDijk).Returns((CarrierRateCard?)null);
        PlannedWithOrder(CarriedBy.Carrier(VanDijk));

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.PricingFailure!.Code.Should().Be("NoRateCard");
    }

    [Fact]
    public async Task Handle_ACarrierShipmentItsCarrierRateCardPrices_ShowsNoPricingFailure()
    {
        PlannedWithOrder(CarriedBy.Carrier(VanDijk));

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.PricingFailure.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AnOwnFleetShipment_ShowsNoPricingFailure_AndReadsNoCarrierRateCard()
    {
        PlannedWithOrder(CarriedBy.OwnFleet);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.PricingFailure.Should().BeNull();
        _carrierRateCards.DidNotReceive().FindFor(Arg.Any<CarrierId>());
    }

    private (TransportOrder Order, Shipment Shipment) PlannedWithOrder(CarriedBy carriedBy)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, carriedBy, sequence: 12);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        _transportOrders.ListAsync(Arg.Any<TransportOrdersByNumbersSpec>(), Arg.Any<CancellationToken>()).Returns([order]);
        return (order, shipment);
    }

    // Van Dijk's Carrier Rate Card, which prices the sample from Rotterdam to Utrecht: up to 2,000 kg at 9.50 per 100 kg.
    private static CarrierRateCard VanDijkRateCard(string laneDestination = "Utrecht") => new(
        new RateCardId("CRC-0001"),
        VanDijk,
        [
            new RateCardVersion(
                2,
                new DateOnly(2026, 1, 1),
                null,
                [new Zone(new ZoneName("Rotterdam"), "NL", ["30"]), new Zone(new ZoneName("Utrecht"), "NL", ["35"]), new Zone(new ZoneName("Groningen"), "NL", ["97"])],
                [new Lane(new ZoneName("Rotterdam"), new ZoneName(laneDestination), [new WeightBreak(2_000m, new Money(9.50m))], new Money(35.00m))]),
        ]);

    [Fact]
    public async Task Handle_PlannedShipments_ListsEachWithWhoCarriesIt()
    {
        var carrier = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.Carrier(VanDijk), sequence: 12);
        var ownFleet = Samples.Planned(Samples.AcceptedOrder(sequence: 8), CarriedBy.OwnFleet, sequence: 13);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([carrier, ownFleet]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().Equal(
            new ShipmentToReleaseSummary(
                "SH-0000012",
                "TO-0000007",
                new ShipperView("SHP-0001", "Brouwer Dranken"),
                new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
                Samples.Now.AddHours(2)),
            new ShipmentToReleaseSummary(
                "SH-0000013",
                "TO-0000008",
                new ShipperView("SHP-0001", "Brouwer Dranken"),
                new CarriedByView(AssignedTo.OwnFleet, null),
                Samples.Now.AddHours(2)));
    }

    [Fact]
    public async Task Handle_NoPlannedShipments_ListsNothing()
    {
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AShipmentThatCameBackRejected_ShowsTheLastCarrierBookingsOutcome()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.Carrier(VanDijk), sequence: 12);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var rejected = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        rejected.Reject(CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday.", new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([rejected]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.LastCarrierBookingOutcome.Should().Be(
            new LastCarrierBookingOutcome(
                CarrierBookingStatus.Rejected, new CarrierView("CAR-0001", "Van Dijk Transport"), CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday."));
    }

    [Fact]
    public async Task Handle_AShipmentWhoseWithdrawnBookingTheCarrierWasNotToldOf_FlagsCarrierNotToldWithTheCause()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var withdrawn = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        withdrawn.Withdraw(new IdempotencyKey("key-withdraw"), new CorrelationId("corr-withdraw"), Samples.Now.AddHours(2));
        withdrawn.ReportCarrierNotTold(503, "Service unavailable.", new CorrelationId("corr-withdraw"), Samples.Now.AddHours(3));
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([withdrawn]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        var outcome = shipments.Should().ContainSingle().Which.LastCarrierBookingOutcome!;
        outcome.Status.Should().Be(CarrierBookingStatus.Withdrawn);
        outcome.CarrierNotTold.Should().BeTrue();
        outcome.CarrierNotToldMessage.Should().Be("Service unavailable.");
    }

    [Fact]
    public async Task Handle_AShipmentWhoseCancelledBookingTheCarrierWasToldOf_DoesNotFlagCarrierNotTold()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var cancelled = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        cancelled.Confirm("VDT-1", null, order.Number!, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        cancelled.Cancel("No show", ExceptionNumber.FromSequence(5), new IdempotencyKey("key-cancel"), new CorrelationId("corr-cancel"), Samples.Now.AddHours(3));
        cancelled.MarkCarrierTold(Samples.Now.AddHours(4));
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([cancelled]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.LastCarrierBookingOutcome!.CarrierNotTold.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AShipmentWhoseConfirmedBookingNorviaCancelled_ShowsItCancelledWithTheReason()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var cancelled = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        cancelled.Confirm("VDT-1", null, order.Number!, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        cancelled.Cancel("Carrier won't make its collection", ExceptionNumber.FromSequence(5), new IdempotencyKey("key-cancel"), new CorrelationId("corr-cancel"), Samples.Now.AddHours(3));
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([cancelled]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.LastCarrierBookingOutcome.Should().Be(
            new LastCarrierBookingOutcome(CarrierBookingStatus.Cancelled, new CarrierView("CAR-0001", "Van Dijk Transport"), null, "Carrier won't make its collection"));
    }

    [Fact]
    public async Task Handle_AShipmentThatCameBackTwice_ShowsOnlyTheMostRecentBookingsOutcome()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.Carrier(VanDijk), sequence: 12);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var withdrawn = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release-1"), Samples.Now.AddHours(1));
        withdrawn.Withdraw(new IdempotencyKey("key-withdraw"), new CorrelationId("corr-withdraw"), Samples.Now.AddHours(2));
        var rejected = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(2), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release-2"), Samples.Now.AddHours(3));
        rejected.Reject(CarrierBookingRejectionReason.NoCapacity, null, new CorrelationId("corr-answer"), Samples.Now.AddHours(4));
        // The spec orders most-recently-requested first; the substitute stands in for the spec, so it must too.
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([rejected, withdrawn]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.LastCarrierBookingOutcome!.Status.Should().Be(CarrierBookingStatus.Rejected);
    }

    [Fact]
    public async Task Handle_AShipmentNeverReleased_HasNoLastCarrierBookingOutcome()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.Carrier(VanDijk), sequence: 12);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().ContainSingle().Which.LastCarrierBookingOutcome.Should().BeNull();
    }
}

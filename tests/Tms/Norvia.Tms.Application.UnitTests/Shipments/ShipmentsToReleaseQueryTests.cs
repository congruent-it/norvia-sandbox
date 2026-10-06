using Ardalis.SharedKernel;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
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
    private readonly ListShipmentsToReleaseHandler _handler;

    public ShipmentsToReleaseQueryTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _carrierBookings.ListAsync(Arg.Any<CarrierBookingsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new ListShipmentsToReleaseHandler(_repository, _carrierBookings, _shippers, _carriers);
    }

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

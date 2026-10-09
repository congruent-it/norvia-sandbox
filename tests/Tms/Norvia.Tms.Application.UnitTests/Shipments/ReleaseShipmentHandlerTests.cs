using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class ReleaseShipmentHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CarrierBookingNumber BookingNumber = CarrierBookingNumber.FromSequence(1);

    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly IRepository<TransportOrder> _transportOrders = Substitute.For<IRepository<TransportOrder>>();
    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly ICarrierBookingNumbers _carrierBookingNumbers = Substitute.For<ICarrierBookingNumbers>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly ICarrierRateCardDirectory _carrierRateCards = Substitute.For<ICarrierRateCardDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly ReleaseShipmentHandler _handler;

    public ReleaseShipmentHandlerTests()
    {
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _carrierBookingNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(BookingNumber);
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard());
        _handler = new ReleaseShipmentHandler(_shipments, _transportOrders, _carrierBookings, _carrierBookingNumbers, _carriers, _carrierRateCards, _time);
    }

    [Fact]
    public async Task Handle_PlannedCarrierShipment_ReleasesIt_AndCreatesItsCarrierBookingRequested()
    {
        var (order, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        result.Value.Should().Be(new ReleaseShipmentResult(
            "SH-0000012",
            "TO-0000007",
            ShipmentStatus.Released,
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "SHP-0001",
            "corr-release",
            "CB-0000001"));
        shipment.Status.Should().Be(ShipmentStatus.Released);
        shipment.LastRelease.Should().Be(new ShipmentRelease(new("corr-release"), Samples.Now.AddHours(3), new("key-rel1"), BookingNumber));
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReleasedEvent>()
            .Which.TransportOrder.Should().BeSameAs(order);
        await _carrierBookings.Received(1).AddAsync(
            Arg.Is<CarrierBooking>(booking =>
                booking.Number == BookingNumber
                && booking.ShipmentId == shipment.Id
                && booking.ShipmentNumber == shipment.Number
                && booking.CarrierId == VanDijk
                && booking.Status == CarrierBookingStatus.Requested
                && booking.CorrelationId == new CorrelationId("corr-release")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlannedOwnFleetShipment_ReleasesIt_WithNoCarrierBookingNumber_AndCreatesNoCarrierBooking()
    {
        PlannedShipment(CarriedBy.OwnFleet);

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        result.Value.CarrierBookingNumber.Should().BeNull();
        await _carrierBookingNumbers.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
        await _carrierBookings.DidNotReceive().AddAsync(Arg.Any<CarrierBooking>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlannedShipment_SavesTheReleaseInOneSave()
    {
        PlannedShipment(CarriedBy.OwnFleet);

        await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        // The save stores the Shipment's change and ShipmentReleased's outbox message in one transaction.
        await _shipments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameReleaseSentAgain_AnswersAsBeforeWithoutReleasingAgain()
    {
        var (order, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));
        shipment.Release(order, new("key-rel1"), new("corr-release"), Samples.Now.AddHours(3), BookingNumber);
        shipment.ClearDomainEvents();

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-retry"), CancellationToken.None);

        result.Value.Should().Be(new ReleaseShipmentResult(
            "SH-0000012",
            "TO-0000007",
            ShipmentStatus.Released,
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "SHP-0001",
            "corr-release",
            "CB-0000001",
            Repeat: true));
        shipment.DomainEvents.Should().BeEmpty();
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _carrierBookings.DidNotReceive().AddAsync(Arg.Any<CarrierBooking>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReleasedShipmentUnderAnotherKey_IsRefusedAsNotPlanned_AndChangesNothing()
    {
        var (order, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));
        shipment.Release(order, new("key-rel1"), new("corr-release"), Samples.Now.AddHours(3), BookingNumber);
        shipment.ClearDomainEvents();

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel2", "corr-release-2"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        var refusal = result.ValidationErrors.Should().ContainSingle().Subject;
        refusal.ErrorCode.Should().Be(nameof(AssignmentRefusalCode.ShipmentNotPlanned));
        refusal.ErrorMessage.Should().Contain("SH-0000012").And.Contain("released");
        shipment.LastRelease!.CorrelationId.Value.Should().Be("corr-release");
        shipment.DomainEvents.Should().BeEmpty();
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("SH-0000099")]
    [InlineData("TO-0000007")]
    public async Task Handle_UnknownOrMalformedNumber_IsRefusedAsNotFound(string number)
    {
        var result = await _handler.Handle(new ReleaseShipmentCommand(number, "key-rel1", "corr-release"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        var refusal = result.ValidationErrors.Should().ContainSingle().Subject;
        refusal.ErrorCode.Should().Be(nameof(AssignmentRefusalCode.ShipmentNotFound));
        refusal.ErrorMessage.Should().Contain(number);
    }

    [Fact]
    public async Task Handle_ShipmentWhoseTransportOrderIsMissing_Throws()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.OwnFleet, sequence: 12);
        _shipments.SingleOrDefaultAsync(Arg.Any<ShipmentToChangeSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);

        var release = async () => await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        await release.Should().ThrowAsync<InvalidOperationException>();
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlannedCarrierShipment_FreezesTheCarrierCostOnTheBooking_AndCarriesItOnShipmentReleased()
    {
        var (_, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        // 910.5 kg (850 + 60.5) is in the band up to 2,000 kg: 910.5 ÷ 100 × 9.50 = 86.4975, so 86.50.
        var expected = new CarrierCost(
            [new ChargeLine(ChargeLineType.Freight, new Money(86.50m))],
            CarrierCostSource.CarrierRateCard(new RateCardVersionReference(new RateCardId("CRC-0001"), 2)),
            Samples.Now.AddHours(3));
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReleasedEvent>().Which.CarrierCost.Should().Be(expected);
        await _carrierBookings.Received(1).AddAsync(Arg.Is<CarrierBooking>(booking => booking.CarrierCost == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CarrierShipmentBelowTheLanesMinimum_PricesTheMinimumChargeAsAnAdjustmentLine()
    {
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard(minimumCharge: 200.00m));
        PlannedShipment(CarriedBy.Carrier(VanDijk));

        await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        await _carrierBookings.Received(1).AddAsync(
            Arg.Is<CarrierBooking>(booking => booking.CarrierCost!.ChargeLines.SequenceEqual(new[]
            {
                new ChargeLine(ChargeLineType.Freight, new Money(86.50m)),
                new ChargeLine(ChargeLineType.MinimumChargeAdjustment, new Money(113.50m)),
            })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlannedOwnFleetShipment_FreezesNoCarrierCost()
    {
        var (_, shipment) = PlannedShipment(CarriedBy.OwnFleet);

        await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReleasedEvent>().Which.CarrierCost.Should().BeNull();
        _carrierRateCards.DidNotReceive().FindFor(Arg.Any<CarrierId>());
    }

    [Fact]
    public async Task Handle_CarrierWithoutACarrierRateCard_IsRefusedAsNoRateCardNamingTheCarrier_AndReleasesNothing()
    {
        _carrierRateCards.FindFor(VanDijk).Returns((CarrierRateCard?)null);
        var (_, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        var refusal = result.ValidationErrors.Should().ContainSingle().Subject;
        refusal.ErrorCode.Should().Be(nameof(AssignmentRefusalCode.NoRateCard));
        refusal.ErrorMessage.Should().Contain("CAR-0001").And.Contain("Van Dijk Transport");
        NothingReleased(shipment);
        await _carrierBookingNumbers.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
        await _carrierBookings.DidNotReceive().AddAsync(Arg.Any<CarrierBooking>(), Arg.Any<CancellationToken>());
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoVersionValidOnTheCollectionDate_IsRefusedAsNoValidRateCardVersion()
    {
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard(validFrom: new DateOnly(2030, 1, 1)));
        var (_, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        RefusalOf(result).ErrorCode.Should().Be(nameof(AssignmentRefusalCode.NoValidRateCardVersion));
        NothingReleased(shipment);
    }

    [Fact]
    public async Task Handle_NoLaneBetweenTheZones_IsRefusedAsNoMatchingLane()
    {
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard(laneDestination: "Groningen"));
        var (_, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        RefusalOf(result).ErrorCode.Should().Be(nameof(AssignmentRefusalCode.NoMatchingLane));
        NothingReleased(shipment);
    }

    [Fact]
    public async Task Handle_WeightAboveTheHighestBreak_IsRefusedAsWeightAboveHighestBreak()
    {
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard(highestBreakKg: 900m));
        var (_, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        RefusalOf(result).ErrorCode.Should().Be(nameof(AssignmentRefusalCode.WeightAboveHighestBreak));
        NothingReleased(shipment);
    }

    [Fact]
    public async Task Handle_SpotPriceAfterTheCarrierRateCardFailed_ReleasesWithASpotCarrierCostOnTheBooking()
    {
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard(laneDestination: "Groningen"));
        var (_, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));
        _carrierBookingNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(BookingNumber);

        var result = await _handler.Handle(
            new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release", new SpotPrice(480m, "Agreed with Van Dijk by phone")), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var expected = CarrierCost.Spot(new Money(480m), "Agreed with Van Dijk by phone", _time.GetUtcNow());
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReleasedEvent>().Which.CarrierCost.Should().Be(expected);
        await _carrierBookings.Received(1).AddAsync(Arg.Is<CarrierBooking>(booking => booking.CarrierCost == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SpotPriceWhenTheCarrierRateCardCanPriceTheShipment_IsRefused_AndReleasesNothing()
    {
        _carrierRateCards.FindFor(VanDijk).Returns(VanDijkRateCard());
        var (_, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(
            new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release", new SpotPrice(480m, "remark")), CancellationToken.None);

        RefusalOf(result).ErrorCode.Should().Be(nameof(AssignmentRefusalCode.CarrierRateCardPricesShipment));
        NothingReleased(shipment);
        await _carrierBookings.DidNotReceive().AddAsync(Arg.Any<CarrierBooking>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SpotPriceForOwnFleet_IsRefused()
    {
        var (_, shipment) = PlannedShipment(CarriedBy.OwnFleet);

        var result = await _handler.Handle(
            new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release", new SpotPrice(480m, "remark")), CancellationToken.None);

        RefusalOf(result).ErrorCode.Should().Be(nameof(AssignmentRefusalCode.SpotPriceNotAllowed));
        NothingReleased(shipment);
    }

    private static ValidationError RefusalOf(Result<ReleaseShipmentResult> result)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        return result.ValidationErrors.Should().ContainSingle().Subject;
    }

    private static void NothingReleased(Shipment shipment)
    {
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        shipment.LastRelease.Should().BeNull();
        shipment.DomainEvents.Should().BeEmpty();
    }

    /// <summary>Van Dijk's Carrier Rate Card, which prices the sample from Rotterdam to Utrecht: up to 2,000 kg at 9.50 per 100 kg.</summary>
    private static CarrierRateCard VanDijkRateCard(
        DateOnly? validFrom = null, string laneDestination = "Utrecht", decimal highestBreakKg = 2_000m, decimal minimumCharge = 35.00m) => new(
        new RateCardId("CRC-0001"),
        VanDijk,
        [
            new RateCardVersion(
                2,
                validFrom ?? new DateOnly(2026, 1, 1),
                null,
                [new Zone(new ZoneName("Rotterdam"), "NL", ["30"]), new Zone(new ZoneName("Utrecht"), "NL", ["35"]), new Zone(new ZoneName("Groningen"), "NL", ["97"])],
                [new Lane(new ZoneName("Rotterdam"), new ZoneName(laneDestination), [new WeightBreak(highestBreakKg, new Money(9.50m))], new Money(minimumCharge))]),
        ]);

    private (TransportOrder Order, Shipment Shipment) PlannedShipment(CarriedBy carriedBy)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, carriedBy, sequence: 12);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>())
            .Returns(shipment);
        _transportOrders.SingleOrDefaultAsync(Arg.Is<TransportOrderByNumberSpec>(s => s.Number == order.Number), Arg.Any<CancellationToken>())
            .Returns(order);
        return (order, shipment);
    }
}

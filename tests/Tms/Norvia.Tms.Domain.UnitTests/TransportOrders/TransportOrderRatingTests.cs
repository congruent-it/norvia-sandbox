using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderRatingTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Accept_ShipperApiBooking_IsNotYetRated()
    {
        var order = Accepted(IntakeChannel.ShipperApi);

        order.RatingResult.Should().Be(RatingResult.NotYetRated);
        order.FreightCharge.Should().BeNull();
    }

    [Fact]
    public void Accept_WmsHandoff_IsNotRatedSeparately()
    {
        var order = Accepted(IntakeChannel.Wms);

        order.RatingResult.Should().Be(RatingResult.NotRatedSeparately);
    }

    [Fact]
    public void Reject_Booking_HasNoRatingResult()
    {
        var order = TransportOrder.Reject(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            [new(RejectionCode.GoodsLinesMissing, "No goods.")],
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-1"),
            ReceivedAt);

        order.RatingResult.Should().BeNull("a Rejected booking is never rated");
    }

    [Fact]
    public void Rate_OrderTheRateCardCovers_IsRatedWithAFreightLine()
    {
        var order = Accepted(IntakeChannel.ShipperApi);

        var rated = order.Rate(RotterdamUtrechtRateCard(), RatedAt);

        rated.Should().BeTrue();
        order.RatingResult.Should().Be(RatingResult.Rated);
        order.FreightCharge.Should().Be(new FreightCharge(
            [new ChargeLine(ChargeLineType.Freight, new Money(105.15m))], // 850 kg ÷ 100 × 12.37 = 105.145, half away from zero
            new RateCardVersionReference(new RateCardId("RC-0001"), 1)));
        order.FreightCharge!.Total.Should().Be(new Money(105.15m));
    }

    [Fact]
    public void Rate_OrderTheRateCardCovers_RaisesTransportOrderRated()
    {
        var order = Accepted(IntakeChannel.ShipperApi);
        order.ClearDomainEvents();

        order.Rate(RotterdamUtrechtRateCard(), RatedAt);

        order.RatedAt.Should().Be(RatedAt);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransportOrderRatedEvent>()
            .Which.TransportOrder.Should().BeSameAs(order);
    }

    [Fact]
    public void Rate_RatedOrder_ChangesNothing()
    {
        var order = Accepted(IntakeChannel.ShipperApi);
        order.Rate(RotterdamUtrechtRateCard(), RatedAt);
        order.ClearDomainEvents();

        var rated = order.Rate(RotterdamUtrechtRateCard(), RatedAt.AddMinutes(1));

        rated.Should().BeFalse("a Freight Charge, once frozen, is never rated again");
        order.RatedAt.Should().Be(RatedAt);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Rate_NotRatedSeparately_StaysNotRatedSeparately()
    {
        var order = Accepted(IntakeChannel.Wms);

        var rated = order.Rate(RotterdamUtrechtRateCard(), RatedAt);

        rated.Should().BeFalse();
        order.RatingResult.Should().Be(RatingResult.NotRatedSeparately);
        order.FreightCharge.Should().BeNull();
    }

    [Fact]
    public void Rate_WithoutARateCard_FailsWithNoRateCard_AndStaysAccepted()
    {
        var order = Accepted(IntakeChannel.ShipperApi);

        var rated = order.Rate(null, RatedAt);

        rated.Should().BeTrue("Rating ran, and its result changed");
        order.RatingResult.Should().Be(RatingResult.RatingFailed);
        order.RatingFailure.Should().Be(new RatingFailure(RatingFailureCode.NoRateCard, "Shipper SHP-0001 has no Rate Card."));
        order.RatingFailedAt.Should().Be(RatedAt);
        order.Status.Should().Be(TransportOrderStatus.Accepted, "a pricing gap never rejects a Shipper's booking");
        order.FreightCharge.Should().BeNull();
        order.RatedAt.Should().BeNull();
    }

    [Fact]
    public void Rate_OrderTheRateCardCantPrice_FailsWithTheRateCardsReason()
    {
        var order = Accepted(IntakeChannel.ShipperApi);

        order.Rate(RotterdamUtrechtRateCard() with { Versions = [RotterdamUtrechtVersion(1, new DateOnly(2027, 1, 1), null, 12.00m)] }, RatedAt);

        order.RatingResult.Should().Be(RatingResult.RatingFailed);
        order.RatingFailure!.Code.Should().Be(RatingFailureCode.NoValidRateCardVersion);
    }

    [Fact]
    public void Rate_RatingFails_RaisesTransportOrderRatingFailed()
    {
        var order = Accepted(IntakeChannel.ShipperApi);
        order.ClearDomainEvents();

        order.Rate(null, RatedAt);

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransportOrderRatingFailedEvent>()
            .Which.TransportOrder.Should().BeSameAs(order);
    }

    [Fact]
    public void Rate_RatingFailedOrder_ChangesNothing()
    {
        var order = Accepted(IntakeChannel.ShipperApi);
        order.Rate(null, RatedAt);
        order.ClearDomainEvents();

        var rated = order.Rate(RotterdamUtrechtRateCard(), RatedAt.AddMinutes(1));

        rated.Should().BeFalse("only a re-rate moves Rating failed on");
        order.RatingResult.Should().Be(RatingResult.RatingFailed);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Rate_Rated_HasNoFailure()
    {
        var order = Accepted(IntakeChannel.ShipperApi);

        order.Rate(RotterdamUtrechtRateCard(), RatedAt);

        order.RatingFailure.Should().BeNull();
        order.RatingFailedAt.Should().BeNull();
    }

    [Fact]
    public void Rate_SeveralVersions_UsesTheOneValidOnTheCollectionDate()
    {
        // The sample's collection starts on 25 September 2026.
        var rateCard = RotterdamUtrechtRateCard() with
        {
            Versions =
            [
                RotterdamUtrechtVersion(1, new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 24), pricePer100Kg: 11.00m),
                RotterdamUtrechtVersion(2, new DateOnly(2026, 9, 25), null, pricePer100Kg: 13.00m),
            ],
        };
        var order = Accepted(IntakeChannel.ShipperApi);

        order.Rate(rateCard, RatedAt);

        order.FreightCharge!.RateCardVersion.Should().Be(new RateCardVersionReference(new RateCardId("RC-0001"), 2));
        order.FreightCharge.Total.Should().Be(new Money(110.50m));
    }

    private static readonly DateTimeOffset RatedAt = ReceivedAt.AddSeconds(2);

    private static RateCardVersion RotterdamUtrechtVersion(int version, DateOnly validFrom, DateOnly? validTo, decimal pricePer100Kg) => new(
        version,
        validFrom,
        validTo,
        [new Zone(new ZoneName("Rotterdam"), "NL", ["30"]), new Zone(new ZoneName("Utrecht"), "NL", ["35"])],
        [new Lane(new ZoneName("Rotterdam"), new ZoneName("Utrecht"), [new WeightBreak(1_000m, new Money(pricePer100Kg))], new Money(45.00m))]);

    private static RateCard RotterdamUtrechtRateCard() => new(
        new RateCardId("RC-0001"),
        new ShipperId("SHP-0001"),
        [
            new RateCardVersion(
                1,
                new DateOnly(2026, 1, 1),
                null,
                [new Zone(new ZoneName("Rotterdam"), "NL", ["30"]), new Zone(new ZoneName("Utrecht"), "NL", ["35"])],
                [new Lane(new ZoneName("Rotterdam"), new ZoneName("Utrecht"), [new WeightBreak(500m, new Money(14.00m)), new WeightBreak(1_000m, new Money(12.37m))], new Money(45.00m))]),
        ]);

    private static TransportOrder Accepted(IntakeChannel channel) =>
        TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            channel,
            new CorrelationId("corr-1"),
            ReceivedAt);
}

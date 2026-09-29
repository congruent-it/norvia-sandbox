using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderSpecsTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ByNumber_Evaluated_FindsOnlyThatTransportOrder()
    {
        var orders = new[] { Accepted(1, Morning), Accepted(2, Morning), Accepted(3, Morning) };

        var found = new TransportOrderByNumberSpec(TransportOrderNumber.FromSequence(2)).Evaluate(orders);

        found.Should().ContainSingle().Which.Number.Should().Be(TransportOrderNumber.FromSequence(2));
    }

    [Fact]
    public void ByStatus_Evaluated_ListsThemInTheOrderTheyWereReceived()
    {
        var orders = new[] { Accepted(1, Morning.AddHours(2)), Accepted(2, Morning), Accepted(3, Morning.AddHours(1)) };

        var listed = new TransportOrdersByStatusSpec(TransportOrderStatus.Accepted).Evaluate(orders);

        listed.Select(order => order.Number!.Value).Should().Equal("TO-0000002", "TO-0000003", "TO-0000001");
    }

    [Fact]
    public void ById_Evaluated_FindsOnlyThatTransportOrder()
    {
        var rejected = Rejected(Morning);
        var orders = new[] { Accepted(1, Morning), rejected, Rejected(Morning) };

        var found = new TransportOrderByIdSpec(rejected.Id).Evaluate(orders);

        found.Should().ContainSingle().Which.Should().BeSameAs(rejected);
    }

    [Fact]
    public void RejectedBookingById_Evaluated_FindsOnlyThatRejectedBooking()
    {
        var rejected = Rejected(Morning);
        var orders = new[] { Accepted(1, Morning), rejected, Rejected(Morning) };

        var found = new RejectedBookingByIdSpec(rejected.Id).Evaluate(orders);

        found.Should().ContainSingle().Which.Should().BeSameAs(rejected);
    }

    [Fact]
    public void RejectedBookingById_AnAcceptedTransportOrdersId_FindsNothing()
    {
        var accepted = Accepted(1, Morning);

        var found = new RejectedBookingByIdSpec(accepted.Id).Evaluate([accepted, Rejected(Morning)]);

        found.Should().BeEmpty();
    }

    [Fact]
    public void ByStatus_Rejected_ListsOnlyTheRejectedBookings()
    {
        var rejected = Rejected(Morning);
        var orders = new[] { Accepted(1, Morning), rejected };

        var listed = new TransportOrdersByStatusSpec(TransportOrderStatus.Rejected).Evaluate(orders);

        listed.Should().ContainSingle().Which.Should().BeSameAs(rejected);
    }

    [Fact]
    public void ByShipperReference_Evaluated_FindsTheShippersAcceptedTransportOrderWithThatReference()
    {
        var held = Accepted(1, Morning);
        var orders = new[]
        {
            Rejected(Morning),
            held,
            TransportOrder.Accept(new ShipperId("SHP-0003"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(2), IntakeChannel.ShipperApi, new CorrelationId("corr-2"), Morning),
            TransportOrder.Accept(new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails() with { ShipperReference = "BD-2026-0002" }, TransportOrderNumber.FromSequence(3), IntakeChannel.ShipperApi, new CorrelationId("corr-3"), Morning),
        };

        var found = new TransportOrderByShipperReferenceSpec(new ShipperId("SHP-0001"), "BD-2026-0001").Evaluate(orders);

        found.Should().ContainSingle().Which.Should().BeSameAs(held);
    }

    [Fact]
    public void NotYetRated_Evaluated_ListsOnlyThoseWaitingForRating_OldestFirst_UpToTheBatchSize()
    {
        var wms = TransportOrder.Accept(new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(9), IntakeChannel.Wms, new CorrelationId("corr-9"), Morning);
        var orders = new[] { Accepted(1, Morning.AddHours(2)), Rejected(Morning), wms, Accepted(2, Morning), Accepted(3, Morning.AddHours(1)) };

        var listed = new TransportOrdersNotYetRatedSpec(skip: 0, batchSize: 2).Evaluate(orders);

        listed.Select(order => order.Number!.Value).Should().Equal("TO-0000002", "TO-0000003");
    }

    [Fact]
    public void NotYetRated_Skipping_ListsTheBatchAfterTheOrdersSkipped()
    {
        var orders = new[] { Accepted(1, Morning.AddHours(2)), Accepted(2, Morning), Accepted(3, Morning.AddHours(1)) };

        var listed = new TransportOrdersNotYetRatedSpec(skip: 1, batchSize: 1).Evaluate(orders);

        listed.Select(order => order.Number!.Value).Should().Equal("TO-0000003");
    }

    [Fact]
    public void RatingFailed_Evaluated_ListsExactlyTheRatingFailedOrders_OldestFirst()
    {
        var wms = TransportOrder.Accept(new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(9), IntakeChannel.Wms, new CorrelationId("corr-9"), Morning);
        var failedLater = Accepted(1, Morning.AddHours(2));
        failedLater.Rate(null, Morning.AddHours(2));
        var failedEarlier = Accepted(2, Morning);
        failedEarlier.Rate(null, Morning);
        var rated = Accepted(3, Morning);
        rated.Rate(
            new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"),
            [
                new RateCardVersion(
                    1,
                    new DateOnly(2020, 1, 1),
                    null,
                    [new Zone(new ZoneName("Rotterdam"), "NL", ["30"]), new Zone(new ZoneName("Utrecht"), "NL", ["35"])],
                    [new Lane(new ZoneName("Rotterdam"), new ZoneName("Utrecht"), [new WeightBreak(24_000m, new Money(10.00m))], new Money(45.00m))]),
            ]),
            Morning);
        rated.RatingResult.Should().Be(RatingResult.Rated);
        var orders = new[] { failedLater, Rejected(Morning), wms, failedEarlier, Accepted(4, Morning), rated };

        var listed = new TransportOrdersRatingFailedSpec().Evaluate(orders);

        listed.Select(order => order.Number!.Value).Should().Equal("TO-0000002", "TO-0000001");
    }

    [Fact]
    public void ToRate_Evaluated_FindsOnlyThatTransportOrder()
    {
        var orders = new[] { Accepted(1, Morning), Accepted(2, Morning) };

        var found = new TransportOrderToRateSpec(TransportOrderNumber.FromSequence(2)).Evaluate(orders);

        found.Should().ContainSingle().Which.Number.Should().Be(TransportOrderNumber.FromSequence(2));
    }

    [Fact]
    public void ByNumbers_Evaluated_FindsOnlyTheTransportOrdersWithThoseNumbers()
    {
        var orders = new[] { Accepted(1, Morning), Accepted(2, Morning), Accepted(3, Morning) };

        var found = new TransportOrdersByNumbersSpec([TransportOrderNumber.FromSequence(1), TransportOrderNumber.FromSequence(3)]).Evaluate(orders);

        found.Select(order => order.Number).Should().BeEquivalentTo([TransportOrderNumber.FromSequence(1), TransportOrderNumber.FromSequence(3)]);
    }

    private static TransportOrder Rejected(DateTimeOffset receivedAt) => TransportOrder.Reject(
        new ShipperId("SHP-0001"),
        TransportOrderTests.SampleDetails(),
        [new RejectionReason(RejectionCode.InactiveShipper, "Inactive.")],
        IntakeChannel.ShipperApi,
        new CorrelationId("corr-rejected"),
        receivedAt);

    private static TransportOrder Accepted(long sequence, DateTimeOffset receivedAt) => TransportOrder.Accept(
        new ShipperId("SHP-0001"),
        TransportOrderTests.SampleDetails(),
        TransportOrderNumber.FromSequence(sequence),
        IntakeChannel.ShipperApi,
        new CorrelationId($"corr-{sequence}"),
        receivedAt);
}

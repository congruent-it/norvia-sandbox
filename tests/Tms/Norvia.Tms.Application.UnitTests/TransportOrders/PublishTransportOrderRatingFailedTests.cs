using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class PublishTransportOrderRatingFailedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_RatingFailedOrder_PublishesTransportOrderRatingFailedWithTheFailure()
    {
        var order = Samples.RatingFailedOrder(sequence: 3, correlationId: "corr-9");

        await new PublishTransportOrderRatingFailed(_outbox).Handle(new TransportOrderRatingFailedEvent(order, order.CorrelationId), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<TransportOrderRatingFailedV1>(e =>
            e.TransportOrderNumber == "TO-0000003"
            && e.ShipperId == "SHP-0001"
            && e.CorrelationId == "corr-9"
            && e.OccurredAt == Samples.Now.AddSeconds(2)
            && e.RatingFailure == new RatingFailureView("NoRateCard", "Shipper SHP-0001 has no Rate Card.")));
    }

    [Fact]
    public async Task Handle_ReRateThatFailsAgain_PublishesOnTheReRatesTrail()
    {
        var order = Samples.RatingFailedOrder(sequence: 3, correlationId: "corr-intake");
        order.ReRate(null, Samples.Now.AddHours(1), new CorrelationId("corr-re-rate"));

        await new PublishTransportOrderRatingFailed(_outbox).Handle(
            new TransportOrderRatingFailedEvent(order, new CorrelationId("corr-re-rate")),
            CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<TransportOrderRatingFailedV1>(e =>
            e.CorrelationId == "corr-re-rate"
            && e.OccurredAt == Samples.Now.AddHours(1)
            && e.ApplicationProperties["correlationId"].Equals("corr-re-rate")));
    }

    [Fact]
    public void TransportOrderRatingFailed_Always_GoesToTheTransportOrderEventsTopicWithTheTrackingKeysAndFailureCode()
    {
        var failed = TransportOrderRatingFailedV1.From(Samples.RatingFailedOrder(sequence: 3, correlationId: "corr-9"), new CorrelationId("corr-9"));

        failed.Topic.Should().Be("transport-order-events");
        failed.EventType.Should().Be("TransportOrderRatingFailed");
        failed.EventVersion.Should().Be(1);
        failed.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TransportOrderRatingFailed",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-9",
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000003",
            ["failureCode"] = "NoRateCard",
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void From_OrderWhoseRatingDidNotFail_Throws(bool rated)
    {
        var act = () => TransportOrderRatingFailedV1.From(rated ? Samples.RatedOrder() : Samples.AcceptedOrder(), new CorrelationId("corr-1"));

        act.Should().Throw<ArgumentException>();
    }
}

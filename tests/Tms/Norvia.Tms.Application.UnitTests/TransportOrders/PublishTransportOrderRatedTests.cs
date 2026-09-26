using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class PublishTransportOrderRatedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_RatedOrder_PublishesTransportOrderRatedWithTheFreightCharge()
    {
        var order = Samples.RatedOrder(sequence: 3, correlationId: "corr-9");
        var expected = new FreightChargeView("EUR", 112.90m, [new ChargeLineView("Freight", 112.90m)], new RateCardVersionView("RC-0001", 3));

        await new PublishTransportOrderRated(_outbox).Handle(new TransportOrderRatedEvent(order), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<TransportOrderRatedV1>(e =>
            e.TransportOrderNumber == "TO-0000003"
            && e.ShipperId == "SHP-0001"
            && e.CorrelationId == "corr-9"
            && e.OccurredAt == Samples.Now.AddSeconds(2)
            && e.FreightCharge == expected));
    }

    [Fact]
    public void TransportOrderRated_Always_GoesToTheTransportOrderEventsTopicWithTheTrackingKeys()
    {
        var rated = TransportOrderRatedV1.From(Samples.RatedOrder(sequence: 3, correlationId: "corr-9"));

        rated.Topic.Should().Be("transport-order-events");
        rated.EventType.Should().Be("TransportOrderRated");
        rated.EventVersion.Should().Be(1);
        rated.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TransportOrderRated",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-9",
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000003",
        });
    }

    [Fact]
    public void From_OrderNotRated_Throws()
    {
        var act = () => TransportOrderRatedV1.From(Samples.AcceptedOrder());

        act.Should().Throw<ArgumentException>();
    }
}

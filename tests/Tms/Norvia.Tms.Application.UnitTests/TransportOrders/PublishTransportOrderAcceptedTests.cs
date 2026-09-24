using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class PublishTransportOrderAcceptedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_AcceptedOrder_PublishesTransportOrderAcceptedWithTheFullTransportOrder()
    {
        var order = Samples.AcceptedOrder(sequence: 3, correlationId: "corr-9");

        await new PublishTransportOrderAccepted(_outbox).Handle(new TransportOrderAcceptedEvent(order), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<TransportOrderAcceptedV1>(e =>
            e.TransportOrderNumber == "TO-0000003"
            && e.CorrelationId == "corr-9"
            && e.OccurredAt == Samples.Now
            && e.TransportOrder == CanonicalTransportOrder.From(order)));
    }

    [Fact]
    public void TransportOrderAccepted_Always_GoesToTheTransportOrderEventsTopicWithTheTrackingKeys()
    {
        var accepted = TransportOrderAcceptedV1.From(Samples.AcceptedOrder(sequence: 3, correlationId: "corr-9"));

        accepted.Topic.Should().Be("transport-order-events");
        accepted.EventType.Should().Be("TransportOrderAccepted");
        accepted.EventVersion.Should().Be(1);
        accepted.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TransportOrderAccepted",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-9",
            ["flow"] = "transport-order-intake",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000003",
        });
    }
}

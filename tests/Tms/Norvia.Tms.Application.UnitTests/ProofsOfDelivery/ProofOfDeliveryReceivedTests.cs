using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ProofsOfDelivery.Record;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.ProofsOfDelivery;

public sealed class ProofOfDeliveryReceivedTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_PublishesProofOfDeliveryReceived_WithWhatWasSent_AndNoFileContent()
    {
        var received = Received();

        await new PublishProofOfDeliveryReceived(_outbox).Handle(received, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ProofOfDeliveryReceivedV1>(e =>
            e.EventType == "ProofOfDeliveryReceived"
            && e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == received.Shipment.TransportOrderNumber.Value
            && e.CarrierId == "CAR-0001"
            && e.CarrierBookingReference == "VDT-1"
            && e.ReceivedBy == "J. Becker"
            && e.DeliveryTime == DeliveryTime
            && e.Condition == "Clean"
            && e.Remark == "Signed at the dock"
            && e.NumberOfFiles == 1
            && !e.ReplacesEarlier
            && e.CorrelationId == "corr-pod"
            && e.OccurredAt == ReceivedAt));
    }

    [Fact]
    public void ProofOfDeliveryReceived_TracksItsKeys_AndFiltersOnWhoCarriesTheShipment()
    {
        var published = ProofOfDeliveryReceivedV1.From(Received());

        published.Topic.Should().Be("shipment-events");
        published.EventVersion.Should().Be(1);
        published.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["flow"] = "carrier-proof-of-delivery",
            ["transportOrderNumber"] = published.TransportOrderNumber,
            ["shipmentNumber"] = "SH-0000012",
            ["partyId"] = "CAR-0001",
            ["assignedTo"] = "Carrier",
            ["eventType"] = "ProofOfDeliveryReceived",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-pod",
        });
    }

    private static ProofOfDeliveryReceivedEvent Received()
    {
        var shipment = ShipmentCarriedTo(ShipmentStatus.Delivered);
        var proofOfDelivery = ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-pod"), ReceivedAt);
        return (ProofOfDeliveryReceivedEvent)proofOfDelivery.DomainEvents.Single();
    }
}

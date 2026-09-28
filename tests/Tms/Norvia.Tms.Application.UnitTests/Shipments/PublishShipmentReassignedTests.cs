using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments.Reassign;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class PublishShipmentReassignedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_ShipmentReassignedToAnotherCarrier_PublishesShipmentReassignedWithWhoCarriedItBefore()
    {
        var reassigned = Reassign(CarriedBy.Carrier(new CarrierId("CAR-0001")), CarriedBy.Carrier(new CarrierId("CAR-0002")));

        await new PublishShipmentReassigned(_outbox).Handle(reassigned, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipmentReassignedV1>(e =>
            e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000003"
            && e.ShipperId == "SHP-0001"
            && e.AssignedTo == "Carrier"
            && e.CarrierId == "CAR-0002"
            && e.PreviousAssignedTo == "Carrier"
            && e.PreviousCarrierId == "CAR-0001"
            && e.CorrelationId == "corr-reassign"
            && e.OccurredAt == Samples.Now.AddHours(3)));
    }

    [Fact]
    public void From_CarrierToOwnFleet_NamesNoCarrierNow_AndTheCarrierBefore()
    {
        var reassigned = ShipmentReassignedV1.From(Reassign(CarriedBy.Carrier(new CarrierId("CAR-0001")), CarriedBy.OwnFleet));

        reassigned.AssignedTo.Should().Be("OwnFleet");
        reassigned.CarrierId.Should().BeNull();
        reassigned.PreviousAssignedTo.Should().Be("Carrier");
        reassigned.PreviousCarrierId.Should().Be("CAR-0001");
    }

    [Fact]
    public void From_OwnFleetToACarrier_NamesTheCarrierNow_AndNoCarrierBefore()
    {
        var reassigned = ShipmentReassignedV1.From(Reassign(CarriedBy.OwnFleet, CarriedBy.Carrier(new CarrierId("CAR-0001"))));

        reassigned.PreviousAssignedTo.Should().Be("OwnFleet");
        reassigned.PreviousCarrierId.Should().BeNull();
    }

    [Fact]
    public void ShipmentReassigned_Always_GoesToTheShipmentEventsTopicWithTheTrackingKeysAndWhoCarriesItNow()
    {
        var reassigned = ShipmentReassignedV1.From(Reassign(CarriedBy.Carrier(new CarrierId("CAR-0001")), CarriedBy.OwnFleet));

        reassigned.Topic.Should().Be("shipment-events");
        reassigned.EventType.Should().Be("ShipmentReassigned");
        reassigned.EventVersion.Should().Be(1);
        reassigned.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ShipmentReassigned",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-reassign",
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000003",
            ["shipmentNumber"] = "SH-0000012",
            ["assignedTo"] = "OwnFleet",
        });
    }

    private static ShipmentReassignedEvent Reassign(CarriedBy from, CarriedBy to)
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 3), from, sequence: 12);
        shipment.Reassign(to, new("key-r1"), new("corr-reassign"), Samples.Now.AddHours(3));
        return (ShipmentReassignedEvent)shipment.DomainEvents.Single();
    }
}

using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class PublishShipmentPlannedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_ShipmentPlannedWithACarrier_PublishesShipmentPlannedWithTheCarrier()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 3), CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12, correlationId: "corr-planning");

        await new PublishShipmentPlanned(_outbox).Handle(new ShipmentPlannedEvent(shipment), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipmentPlannedV1>(e =>
            e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000003"
            && e.ShipperId == "SHP-0001"
            && e.AssignedTo == "Carrier"
            && e.CarrierId == "CAR-0001"
            && e.CorrelationId == "corr-planning"
            && e.OccurredAt == Samples.Now.AddHours(2)));
    }

    [Fact]
    public void From_OwnFleetShipment_NamesNoCarrier()
    {
        var planned = ShipmentPlannedV1.From(Samples.Planned(Samples.AcceptedOrder(), CarriedBy.OwnFleet));

        planned.AssignedTo.Should().Be("OwnFleet");
        planned.CarrierId.Should().BeNull();
    }

    [Fact]
    public void ShipmentPlanned_Always_GoesToTheShipmentEventsTopicWithTheTrackingKeysAndWhoCarriesIt()
    {
        var planned = ShipmentPlannedV1.From(
            Samples.Planned(Samples.AcceptedOrder(sequence: 3), CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12, correlationId: "corr-planning"));

        planned.Topic.Should().Be("shipment-events");
        planned.EventType.Should().Be("ShipmentPlanned");
        planned.EventVersion.Should().Be(1);
        planned.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ShipmentPlanned",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-planning",
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000003",
            ["shipmentNumber"] = "SH-0000012",
            ["assignedTo"] = "Carrier",
        });
    }

    [Fact]
    public void ShipmentPlanned_OwnFleet_SaysSoInItsProperties()
    {
        var planned = ShipmentPlannedV1.From(Samples.Planned(Samples.AcceptedOrder(), CarriedBy.OwnFleet));

        planned.ApplicationProperties.Should().Contain("assignedTo", "OwnFleet");
    }
}

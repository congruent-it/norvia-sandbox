using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class PublishShipmentReleasedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_CarrierShipmentReleased_PublishesShipmentReleasedWithTheCarrierAndTheCarrierBookingNumber()
    {
        var released = Release(CarriedBy.Carrier(new CarrierId("CAR-0001")), CarrierBookingNumber.FromSequence(1));
        var transportOrder = Samples.CanonicalTransportOrder() with { TransportOrderNumber = "TO-0000003" };

        await new PublishShipmentReleased(_outbox).Handle(released, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipmentReleasedV1>(e =>
            e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000003"
            && e.ShipperId == "SHP-0001"
            && e.AssignedTo == "Carrier"
            && e.CarrierId == "CAR-0001"
            && e.CarrierBookingNumber == "CB-0000001"
            && e.CorrelationId == "corr-release"
            && e.OccurredAt == Samples.Now.AddHours(3)
            && e.TransportOrder.Equals(transportOrder)));
    }

    [Fact]
    public void From_OwnFleetShipment_NamesNoCarrier_AndNoCarrierBookingNumber()
    {
        var released = ShipmentReleasedV1.From(Release(CarriedBy.OwnFleet, carrierBookingNumber: null));

        released.AssignedTo.Should().Be("OwnFleet");
        released.CarrierId.Should().BeNull();
        released.CarrierBookingNumber.Should().BeNull();
    }

    [Fact]
    public void From_CarrierShipmentWithACarrierCost_CarriesItWithItsLinesTotalAndSource()
    {
        var cost = new CarrierCost(
            [new ChargeLine(ChargeLineType.Freight, new Money(11.00m)), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, new Money(24.00m))],
            CarrierCostSource.CarrierRateCard(new RateCardVersionReference(new RateCardId("CRC-0001"), 2)),
            Samples.Now.AddHours(3));

        var released = ShipmentReleasedV1.From(Release(CarriedBy.Carrier(new CarrierId("CAR-0001")), CarrierBookingNumber.FromSequence(1), cost));

        released.CarrierCost.Should().Be(new CarrierCostView(
            "EUR",
            35.00m,
            [new ChargeLineView("Freight", 11.00m), new ChargeLineView("MinimumChargeAdjustment", 24.00m)],
            new CarrierCostSourceView("CarrierRateCard", new RateCardVersionView("CRC-0001", 2)),
            Samples.Now.AddHours(3)));
    }

    [Fact]
    public void From_OwnFleetShipment_CarriesNoCarrierCost()
    {
        ShipmentReleasedV1.From(Release(CarriedBy.OwnFleet, carrierBookingNumber: null)).CarrierCost.Should().BeNull();
    }

    [Fact]
    public void ShipmentReleased_ForACarrierShipment_TracksTheCarrierBookingNumberToo()
    {
        var released = ShipmentReleasedV1.From(Release(CarriedBy.Carrier(new CarrierId("CAR-0001")), CarrierBookingNumber.FromSequence(1)));

        released.Topic.Should().Be("shipment-events");
        released.EventType.Should().Be("ShipmentReleased");
        released.EventVersion.Should().Be(1);
        released.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ShipmentReleased",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-release",
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000003",
            ["shipmentNumber"] = "SH-0000012",
            ["carrierBookingNumber"] = "CB-0000001",
            ["assignedTo"] = "Carrier",
        });
    }

    [Fact]
    public void ShipmentReleased_ForAnOwnFleetShipment_LeavesOutTheCarrierBookingNumberKey()
    {
        var released = ShipmentReleasedV1.From(Release(CarriedBy.OwnFleet, carrierBookingNumber: null));

        released.ApplicationProperties.Should().NotContainKey("carrierBookingNumber");
    }

    private static ShipmentReleasedEvent Release(CarriedBy carriedBy, CarrierBookingNumber? carrierBookingNumber, CarrierCost? carrierCost = null)
    {
        var order = Samples.AcceptedOrder(sequence: 3);
        var shipment = Samples.Planned(order, carriedBy, sequence: 12);
        shipment.Release(order, new("key-rel1"), new("corr-release"), Samples.Now.AddHours(3), carrierBookingNumber, carrierCost);
        return (ShipmentReleasedEvent)shipment.DomainEvents.Single();
    }
}

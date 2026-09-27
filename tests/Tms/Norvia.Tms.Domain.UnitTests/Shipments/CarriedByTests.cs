using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

public sealed class CarriedByTests
{
    [Fact]
    public void Carrier_SameCarrier_IsEqual()
    {
        CarriedBy.Carrier(new CarrierId("CAR-0001")).Should().Be(CarriedBy.Carrier(new CarrierId("CAR-0001")))
            .And.NotBe(CarriedBy.Carrier(new CarrierId("CAR-0002")))
            .And.NotBe(CarriedBy.OwnFleet);
    }

    [Fact]
    public void ToString_NamesOwnFleetOrTheCarrier()
    {
        CarriedBy.OwnFleet.ToString().Should().Be("Own fleet");
        CarriedBy.Carrier(new CarrierId("CAR-0001")).ToString().Should().Be("CAR-0001");
    }

    [Fact]
    public void CorrelationIdNew_StartsANewTrailEachTime()
    {
        CorrelationId.New().Should().NotBe(CorrelationId.New());
    }
}

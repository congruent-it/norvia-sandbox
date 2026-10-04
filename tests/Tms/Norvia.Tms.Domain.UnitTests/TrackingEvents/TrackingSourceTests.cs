using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.UnitTests.TrackingEvents;

public sealed class TrackingSourceTests
{
    [Fact]
    public void Carrier_NamesTheCarrier()
    {
        var source = TrackingSource.Carrier(new CarrierId("CAR-0001"));

        source.CarrierId.Should().Be(new CarrierId("CAR-0001"));
        source.IsOwnFleet.Should().BeFalse();
        source.ToString().Should().Be("CAR-0001");
    }

    [Fact]
    public void OwnFleet_NamesNoCarrier()
    {
        TrackingSource.OwnFleet.CarrierId.Should().BeNull();
        TrackingSource.OwnFleet.IsOwnFleet.Should().BeTrue();
        TrackingSource.OwnFleet.ToString().Should().Be("Own fleet");
    }

    [Fact]
    public void TwoSourcesOfTheSameCarrier_AreEqual() =>
        TrackingSource.Carrier(new CarrierId("CAR-0001")).Should().Be(TrackingSource.Carrier(new CarrierId("CAR-0001")));
}

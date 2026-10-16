using System.Text.Json;
using Norvia.Simulator.Domain.Drivers;

namespace Norvia.Simulator.Domain.UnitTests.Drivers;

public sealed class DriverFunnelTests
{
    private static readonly DriverShares Nothing = new(0, 0, 0, 0);

    private static IReadOnlyList<DriverJourney> Plan(DriverShares shares, int count = 100, int seed = 7) => DriverFunnel.Plan(shares, seed, count);

    [Fact]
    public void Plan_GivesOneJourneyPerShipment_InOrder()
    {
        Plan(Nothing, count: 12).Select(journey => journey.Index).Should().Equal(Enumerable.Range(0, 12));
    }

    [Fact]
    public void Plan_NoShipments_GivesNoJourneys()
    {
        Plan(new DriverShares(), count: 0).Should().BeEmpty();
    }

    [Fact]
    public void Plan_NothingGoingWrong_DeliversEveryShipmentClean()
    {
        var journeys = Plan(Nothing, count: 20);

        journeys.Should().OnlyContain(journey => journey.Delivered && !journey.CollectionFailed && !journey.DeliveryFailed && !journey.Damaged && !journey.Reservations);
    }

    [Fact]
    public void Plan_TheSameSeed_GivesTheSameJourneys_AndAnotherSeedOtherOnes()
    {
        var shares = new DriverShares(20, 20, 10, 30);

        JsonSerializer.Serialize(Plan(shares, seed: 42)).Should().Be(JsonSerializer.Serialize(Plan(shares, seed: 42)));
        JsonSerializer.Serialize(Plan(shares, seed: 1)).Should().NotBe(JsonSerializer.Serialize(Plan(shares, seed: 2)));
    }

    [Fact]
    public void Plan_EachShare_IsExactlyThatShare()
    {
        var journeys = Plan(new DriverShares(CollectionFailedPercent: 10, DeliveryFailedPercent: 20, DamagedPercent: 5, ReservationsPercent: 25), count: 100);

        journeys.Count(journey => journey.CollectionFailed).Should().Be(10);
        journeys.Count(journey => journey.DeliveryFailed).Should().Be(20);
        journeys.Count(journey => journey.Damaged).Should().Be(5);
        journeys.Count(journey => journey.Reservations).Should().Be(18); // 25% of the 70 delivered, rounded
    }

    [Fact]
    public void Plan_AShipmentWhoseCollectionFailed_NeverFailsItsDeliveryOrHasReservations()
    {
        var journeys = Plan(new DriverShares(50, 50, 0, 100), count: 40);

        journeys.Where(journey => journey.CollectionFailed).Should().OnlyContain(journey => !journey.DeliveryFailed && !journey.Reservations && !journey.Delivered);
    }

    [Fact]
    public void Plan_OnlyDeliveredShipmentsHaveReservations()
    {
        var journeys = Plan(new DriverShares(20, 20, 0, 100), count: 50);

        journeys.Where(journey => journey.Reservations).Should().OnlyContain(journey => journey.Delivered);
        journeys.Count(journey => journey.Reservations).Should().Be(journeys.Count(journey => journey.Delivered));
    }

    [Fact]
    public void Count_TalliesTheJourneys()
    {
        var counts = DriverFunnel.Count(Plan(new DriverShares(10, 10, 5, 10), count: 100));

        counts.Should().Be(new DriverFunnelCounts(100, 10, 10, 5, 80, 8));
    }

    [Fact]
    public void Problems_SharesWithinRange_AreNone()
    {
        new DriverShares().Problems().Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, "collection fails")]
    [InlineData(0, 101, 0, 0, "delivery fails")]
    [InlineData(0, 0, -1, 0, "damage")]
    [InlineData(0, 0, 0, 101, "reservations")]
    public void Problems_ASharePercentOutOfRange_IsNamed(int collection, int delivery, int damage, int reservations, string named)
    {
        new DriverShares(collection, delivery, damage, reservations).Problems().Should().ContainSingle().Which.Should().Contain(named).And.Contain("0 to 100");
    }

    [Fact]
    public void Problems_MoreFailuresThanShipments_IsNamed()
    {
        new DriverShares(CollectionFailedPercent: 60, DeliveryFailedPercent: 50).Problems().Should().ContainSingle().Which.Should().Contain("can't be more than all");
    }
}

using Norvia.Simulator.Domain.Trips;

namespace Norvia.Simulator.Domain.UnitTests.Trips;

public sealed class TripSelectionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 20)]
    [InlineData(50, 10)]
    [InlineData(25, 5)]
    [InlineData(33, 7)]
    public void Pick_IsExactlyTheShareAskedFor(int percent, int expected)
    {
        TripSelection.Pick(seed: 7, count: 20, percent).Count(onATrip => onATrip).Should().Be(expected);
    }

    [Fact]
    public void Pick_GivesOneAnswerPerShipment()
    {
        TripSelection.Pick(7, 12, 50).Should().HaveCount(12);
        TripSelection.Pick(7, 0, 50).Should().BeEmpty();
    }

    [Fact]
    public void Pick_TheSameSeed_PicksTheSameShipments_AndAnotherSeedOtherOnes()
    {
        TripSelection.Pick(42, 40, 50).Should().Equal(TripSelection.Pick(42, 40, 50));
        TripSelection.Pick(1, 40, 50).Should().NotEqual(TripSelection.Pick(2, 40, 50));
    }

    [Fact]
    public void Pick_ANegativeCount_Throws()
    {
        var pick = () => TripSelection.Pick(7, -1, 50);

        pick.Should().Throw<ArgumentOutOfRangeException>();
    }
}

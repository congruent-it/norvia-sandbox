using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Domain.UnitTests.Plans;

public sealed class PickupSlotTests
{
    // 09:30 in Rotterdam in October (UTC+2 until 25 October 2026).
    private static readonly DateTimeOffset MorningLocal = new(2026, 10, 13, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_ALaterDay_StartsAtTheHourOfThatDay()
    {
        var window = new PickupSlot(DayOffset: 2, FromHour: 8, Hours: 3).Resolve(MorningLocal);

        window.From.Should().Be(new DateTime(2026, 10, 15, 8, 0, 0));
        window.Until.Should().Be(new DateTime(2026, 10, 15, 11, 0, 0));
    }

    [Fact]
    public void Resolve_TodayAtAnEarlyHour_StartsAtTheNextFullHour()
    {
        var window = new PickupSlot(0, FromHour: 8, Hours: 2).Resolve(MorningLocal);

        window.From.Should().Be(new DateTime(2026, 10, 13, 10, 0, 0));
        window.Until.Should().Be(new DateTime(2026, 10, 13, 12, 0, 0));
    }

    [Fact]
    public void Resolve_TodayAtALaterHour_KeepsThePlannedHour()
    {
        var window = new PickupSlot(0, FromHour: 14, Hours: 2).Resolve(MorningLocal);

        window.From.Should().Be(new DateTime(2026, 10, 13, 14, 0, 0));
    }

    [Fact]
    public void Resolve_TodayWhenTheDayIsOver_MovesToTomorrow()
    {
        var evening = new DateTimeOffset(2026, 10, 13, 18, 30, 0, TimeSpan.Zero);

        var window = new PickupSlot(0, FromHour: 9, Hours: 2).Resolve(evening);

        window.From.Should().Be(new DateTime(2026, 10, 14, 9, 0, 0));
    }
}

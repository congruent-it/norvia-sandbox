using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Exceptions;

/// <summary>A Shipment is late when an ETA is after the delivery window's latest time, or when that time plus the grace has passed (P7).</summary>
public sealed class LateDeliveryRulesTests
{
    private static readonly DateTimeOffset Latest = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Window = new(Latest.AddHours(-4), Latest);

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-60, false)]
    public void IsEtaLate_IsTrueOnlyForAnEtaAfterTheWindowsLatestTime(int minutesFromLatest, bool expected) =>
        LateDeliveryRules.IsEtaLate(Latest.AddMinutes(minutesFromLatest), Window).Should().Be(expected);

    [Fact]
    public void IsEtaLate_WithoutAnEtaOrAWindowOrALatestTime_IsFalse()
    {
        LateDeliveryRules.IsEtaLate(null, Window).Should().BeFalse();
        LateDeliveryRules.IsEtaLate(Latest.AddDays(1), null).Should().BeFalse();
        LateDeliveryRules.IsEtaLate(Latest.AddDays(1), new TimeWindow(Latest, null)).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(5, true)]
    public void HasWindowPassed_WithNoGrace_IsTrueFromTheWindowsLatestTime(int minutesFromLatest, bool expected) =>
        new LateDeliveryRules(TimeSpan.Zero).HasWindowPassed(Window, Latest.AddMinutes(minutesFromLatest)).Should().Be(expected);

    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    public void HasWindowPassed_HonoursTheGrace(int minutesFromLatest, bool expected) =>
        new LateDeliveryRules(TimeSpan.FromHours(2)).HasWindowPassed(Window, Latest.AddMinutes(minutesFromLatest)).Should().Be(expected);

    [Fact]
    public void HasWindowPassed_WithoutAWindowOrALatestTime_IsFalse()
    {
        var rules = new LateDeliveryRules(TimeSpan.Zero);

        rules.HasWindowPassed(null, Latest.AddYears(1)).Should().BeFalse();
        rules.HasWindowPassed(new TimeWindow(Latest, null), Latest.AddYears(1)).Should().BeFalse();
    }
}

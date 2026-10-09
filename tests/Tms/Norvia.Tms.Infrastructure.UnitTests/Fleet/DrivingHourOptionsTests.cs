using Norvia.Tms.Infrastructure.Fleet;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

public sealed class DrivingHourOptionsTests
{
    [Fact]
    public void ToLimits_ByDefault_IsNineHoursExtendedToTenTwiceAWeek_FiftySixAWeek_NinetyAFortnight()
    {
        var limits = new DrivingHourOptions().ToLimits();

        limits.Daily.Should().Be(TimeSpan.FromHours(9));
        limits.ExtendedDaily.Should().Be(TimeSpan.FromHours(10));
        limits.ExtensionsPerWeek.Should().Be(2);
        limits.Weekly.Should().Be(TimeSpan.FromHours(56));
        limits.Fortnightly.Should().Be(TimeSpan.FromHours(90));
    }

    [Fact]
    public void ToLimits_TightSettings_AreUsedAsGiven()
    {
        var limits = new DrivingHourOptions { DailyHours = 4.5m, ExtendedDailyHours = 5m, ExtensionsPerWeek = 1, WeeklyHours = 12m, FortnightlyHours = 20m }.ToLimits();

        limits.Daily.Should().Be(TimeSpan.FromHours(4.5));
        limits.Fortnightly.Should().Be(TimeSpan.FromHours(20));
    }

    [Theory]
    [InlineData(9, 8, 56, 90, false)]
    [InlineData(9, 10, 56, 50, false)]
    [InlineData(9, 10, 56, 90, true)]
    public void IsConsistent_ExtendedBelowDailyOrFortnightBelowWeek_IsNot(decimal daily, decimal extended, decimal weekly, decimal fortnightly, bool consistent)
    {
        new DrivingHourOptions { DailyHours = daily, ExtendedDailyHours = extended, WeeklyHours = weekly, FortnightlyHours = fortnightly }
            .IsConsistent.Should().Be(consistent);
    }
}

using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.UnitTests.Runs;

public sealed class SimulationSettingsTests
{
    private static readonly SimulationLimits Limits = new(MaxOrdersPerRun: 100, MaxConcurrentRequests: 4);

    private static SimulationSettings Settings(int orders = 10, int bad = 10, int today = 30, RunMode mode = RunMode.Burst, int windowMinutes = 0) =>
        new(orders, bad, today, 1, mode, TimeSpan.FromMinutes(windowMinutes));

    [Fact]
    public void Problems_SettingsWithinTheLimits_AreNone()
    {
        Settings().Problems(Limits).Should().BeEmpty();
    }

    [Fact]
    public void Problems_MoreOrdersThanTheCap_RefusesThePlan()
    {
        Settings(orders: 101).Problems(Limits).Should().ContainSingle().Which.Should().Contain("at most 100");
    }

    [Theory]
    [InlineData(-1, 50, 5, "assigns")]
    [InlineData(101, 50, 5, "assigns")]
    [InlineData(80, -1, 5, "Own fleet")]
    [InlineData(80, 101, 5, "Own fleet")]
    [InlineData(80, 50, -1, "reassigns")]
    [InlineData(80, 50, 101, "reassigns")]
    public void Problems_APlannerShareOutOfRange_IsNamed(int assigned, int ownFleet, int reassign, string named)
    {
        var settings = new SimulationSettings(10, 10, 30, 1, RunMode.Burst, TimeSpan.Zero, null, assigned, ownFleet, reassign);

        settings.Problems(Limits).Should().ContainSingle().Which.Should().Contain(named).And.Contain("0 to 100");
    }

    [Fact]
    public void Problems_AnOrderCountOfTheCap_IsAllowed()
    {
        Settings(orders: 100).Problems(Limits).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Problems_NoOrders_AreRefused(int orders)
    {
        Settings(orders: orders).Problems(Limits).Should().ContainSingle().Which.Should().Contain("at least one");
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(101, 30)]
    [InlineData(10, -1)]
    [InlineData(10, 101)]
    public void Problems_ASharePastZeroToOneHundred_IsRefused(int bad, int today)
    {
        Settings(bad: bad, today: today).Problems(Limits).Should().ContainSingle().Which.Should().Contain("percentage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void Problems_APacedRunWithoutAUsableWindow_IsRefused(int minutes)
    {
        Settings(mode: RunMode.Paced, windowMinutes: minutes).Problems(Limits).Should().ContainSingle().Which.Should().Contain("minutes");
    }

    [Fact]
    public void Problems_ABurstRun_NeedsNoWindow()
    {
        Settings(mode: RunMode.Burst, windowMinutes: 0).Problems(Limits).Should().BeEmpty();
    }

    [Fact]
    public void Presets_EachFillsTheSettingsWithTheChosenSeed()
    {
        foreach (var preset in Presets.All)
        {
            var settings = preset.ToSettings(99);
            settings.Seed.Should().Be(99);
            settings.PresetName.Should().Be(preset.Name);
            settings.OrderCount.Should().Be(preset.OrderCount);
        }

        Presets.Find("Quiet Monday").Should().NotBeNull();
        Presets.Find("nope").Should().BeNull();
    }
}

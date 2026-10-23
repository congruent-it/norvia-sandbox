using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.ServiceDefaults.UnitTests;

public sealed class FaultSwitchboardTests
{
    private static readonly FaultDefinition Unavailable = new("driver-app-unavailable", "driver-app", "Refuses every Trip.");
    private static readonly FaultDefinition Slow = new("tms-slow", "tms", "Answers slowly.");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly FaultOptions _options = new() { MaxDuration = TimeSpan.FromMinutes(5), DefaultDuration = TimeSpan.FromMinutes(2) };

    private FaultSwitchboard Faults() => new([Unavailable, Slow], Options.Create(_options), _time);

    [Fact]
    public void AFault_IsOffByDefault()
    {
        var faults = Faults();

        faults.IsOn(Unavailable.Name).Should().BeFalse();
        faults.List().Should().OnlyContain(fault => !fault.On && fault.Until == null && fault.RemainingSeconds == null);
    }

    [Fact]
    public void List_ShowsEveryFault_ByOwnerThenName()
    {
        Faults().List().Select(fault => fault.Name).Should().Equal("driver-app-unavailable", "tms-slow");
    }

    [Fact]
    public void SwitchOn_TurnsTheFaultOn_UntilItsExpiry()
    {
        var faults = Faults();

        var result = faults.SwitchOn(Unavailable.Name, TimeSpan.FromMinutes(1), out var status);

        result.Should().Be(SwitchOnResult.Switched);
        faults.IsOn(Unavailable.Name).Should().BeTrue();
        status.Should().Be(new FaultStatus(Unavailable.Name, "driver-app", Unavailable.Description, true, _time.GetUtcNow().AddMinutes(1), 60));
        faults.IsOn(Slow.Name).Should().BeFalse("a switch affects only its own fault");
    }

    [Fact]
    public void AFault_ExpiresByItself()
    {
        var faults = Faults();
        faults.SwitchOn(Unavailable.Name, TimeSpan.FromMinutes(1), out _);

        _time.Advance(TimeSpan.FromSeconds(59));
        faults.IsOn(Unavailable.Name).Should().BeTrue();
        _time.Advance(TimeSpan.FromSeconds(1));

        faults.IsOn(Unavailable.Name).Should().BeFalse();
        faults.List().Should().OnlyContain(fault => !fault.On);
    }

    [Fact]
    public void List_CountsDownTheRemainingSeconds()
    {
        var faults = Faults();
        faults.SwitchOn(Unavailable.Name, TimeSpan.FromMinutes(1), out _);
        _time.Advance(TimeSpan.FromSeconds(20.5));

        faults.List().Single(fault => fault.Name == Unavailable.Name).RemainingSeconds.Should().Be(40);
    }

    [Fact]
    public void SwitchOn_WithoutADuration_UsesTheDefault()
    {
        var faults = Faults();

        faults.SwitchOn(Unavailable.Name, duration: null, out var status);

        status!.RemainingSeconds.Should().Be(120);
    }

    [Fact]
    public void SwitchOn_Again_StartsTheTimeOver()
    {
        var faults = Faults();
        faults.SwitchOn(Unavailable.Name, TimeSpan.FromMinutes(1), out _);
        _time.Advance(TimeSpan.FromSeconds(50));

        faults.SwitchOn(Unavailable.Name, TimeSpan.FromMinutes(1), out var status);

        status!.RemainingSeconds.Should().Be(60);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(301)]
    public void SwitchOn_WithADurationOutsideTheLimits_RefusesAndLeavesTheFaultOff(int seconds)
    {
        var faults = Faults();

        var result = faults.SwitchOn(Unavailable.Name, TimeSpan.FromSeconds(seconds), out var status);

        result.Should().Be(SwitchOnResult.DurationNotAllowed);
        status.Should().BeNull();
        faults.IsOn(Unavailable.Name).Should().BeFalse();
    }

    [Fact]
    public void SwitchOn_ForTheLongestAllowedDuration_Works()
    {
        Faults().SwitchOn(Unavailable.Name, TimeSpan.FromMinutes(5), out _).Should().Be(SwitchOnResult.Switched);
    }

    [Fact]
    public void SwitchOn_AnUnknownFault_IsRefused()
    {
        var faults = Faults();

        faults.SwitchOn("nope", TimeSpan.FromMinutes(1), out var status).Should().Be(SwitchOnResult.UnknownFault);

        status.Should().BeNull();
        faults.IsOn("nope").Should().BeFalse();
    }

    [Fact]
    public void SwitchOff_TurnsTheFaultOffAtOnce()
    {
        var faults = Faults();
        faults.SwitchOn(Unavailable.Name, TimeSpan.FromMinutes(1), out _);

        faults.SwitchOff(Unavailable.Name, out var status).Should().BeTrue();

        faults.IsOn(Unavailable.Name).Should().BeFalse();
        status!.On.Should().BeFalse();
    }

    [Fact]
    public void SwitchOff_AFaultThatIsAlreadyOff_Succeeds()
    {
        Faults().SwitchOff(Unavailable.Name, out var status).Should().BeTrue();

        status!.On.Should().BeFalse();
    }

    [Fact]
    public void SwitchOff_AnUnknownFault_IsRefused()
    {
        Faults().SwitchOff("nope", out var status).Should().BeFalse();

        status.Should().BeNull();
    }
}

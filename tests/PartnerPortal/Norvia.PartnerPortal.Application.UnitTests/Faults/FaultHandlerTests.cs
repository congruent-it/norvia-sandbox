using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Application.Faults.List;
using Norvia.PartnerPortal.Application.Faults.Switch;

namespace Norvia.PartnerPortal.Application.UnitTests.Faults;

public sealed class FaultHandlerTests
{
    private static readonly FaultView Off = new("driver-app-unavailable", "driver-app", "Refuses every Trip.", false, null);

    private readonly IFaultSwitches _faults = Substitute.For<IFaultSwitches>();

    [Fact]
    public async Task List_ReportsEveryFaultTheApplicationsOwn()
    {
        _faults.ListAsync(Arg.Any<CancellationToken>()).Returns([Off]);

        var listed = await new ListFaultsHandler(_faults).Handle(new ListFaultsQuery(), CancellationToken.None);

        listed.Should().Equal(Off);
    }

    [Fact]
    public async Task Switch_WithADuration_SwitchesTheFaultOn_AndReportsHowItNowStands()
    {
        var on = Off with { On = true, Until = new DateTimeOffset(2026, 10, 23, 9, 2, 0, TimeSpan.Zero) };
        _faults.SwitchAsync("driver-app", "driver-app-unavailable", TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>()).Returns(on);

        var view = await new SwitchFaultHandler(_faults).Handle(new SwitchFaultCommand("driver-app", "driver-app-unavailable", TimeSpan.FromMinutes(2)), CancellationToken.None);

        view.Should().Be(on);
    }

    [Fact]
    public async Task Switch_WithoutADuration_SwitchesTheFaultOff()
    {
        _faults.SwitchAsync("driver-app", "driver-app-unavailable", null, Arg.Any<CancellationToken>()).Returns(Off);

        var view = await new SwitchFaultHandler(_faults).Handle(new SwitchFaultCommand("driver-app", "driver-app-unavailable", null), CancellationToken.None);

        view.On.Should().BeFalse();
        await _faults.Received(1).SwitchAsync("driver-app", "driver-app-unavailable", null, Arg.Any<CancellationToken>());
    }
}

public sealed class FaultViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RemainingAt_WhileTheFaultIsOn_IsTheTimeLeft()
    {
        var fault = new FaultView("f", "o", "d", true, Now.AddSeconds(90));

        fault.RemainingAt(Now).Should().Be(TimeSpan.FromSeconds(90));
    }

    [Theory]
    [InlineData(false, 90)]
    [InlineData(true, 0)]
    [InlineData(true, -5)]
    public void RemainingAt_WhenTheFaultIsOffOrHasRunOut_IsNull(bool on, int secondsLeft)
    {
        var fault = new FaultView("f", "o", "d", on, Now.AddSeconds(secondsLeft));

        fault.RemainingAt(Now).Should().BeNull();
    }

    [Fact]
    public void RemainingAt_WithoutAnExpiry_IsNull()
    {
        new FaultView("f", "o", "d", true, null).RemainingAt(Now).Should().BeNull();
    }
}

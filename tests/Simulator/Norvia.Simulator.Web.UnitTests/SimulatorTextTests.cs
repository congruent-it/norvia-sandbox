using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.UnitTests;

public sealed class SimulatorTextTests
{
    [Fact]
    public void Of_EveryOutcomeStatusKindAndAnswer_HasAWording()
    {
        Enum.GetValues<LogOutcome>().Select(SimulatorText.Of).Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text));
        Enum.GetValues<SimulationRunStatus>().Select(SimulatorText.Of).Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text));
        Enum.GetValues<BadBookingKind>().Select(SimulatorText.Of).Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text));
        Enum.GetValues<ExpectedAnswer>().Select(SimulatorText.Of).Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData(null, false, "Not assigned")]
    [InlineData(PlannerIntent.Carrier, false, "A Carrier")]
    [InlineData(PlannerIntent.OwnFleetIfItFits, false, "Own fleet if it fits, else a Carrier")]
    [InlineData(PlannerIntent.Carrier, true, "A Carrier, then reassigned")]
    public void Of_WhatThePlannerMeansToDo_SaysSo(PlannerIntent? intent, bool reassign, string expected)
    {
        SimulatorText.Of(intent, reassign).Should().Be(expected);
    }

    [Fact]
    public void Of_AnUnknownIntent_Throws()
    {
        var wording = () => SimulatorText.Of((PlannerIntent)99, reassign: false);

        wording.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Of_AnUnknownValue_Throws()
    {
        var wording = () => SimulatorText.Of((LogOutcome)99);

        wording.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(RunMode.Burst, 0, "Burst")]
    [InlineData(RunMode.Paced, 10, "Paced over 10 min")]
    public void Of_ARunMode_SaysHowItSends(RunMode mode, int minutes, string expected)
    {
        SimulatorText.Of(mode, TimeSpan.FromMinutes(minutes)).Should().Be(expected);
    }

    [Fact]
    public void Time_IsNorviasOwnLocalTime()
    {
        // 10:00 UTC is 12:00 in Rotterdam in October.
        SimulatorText.Time(new DateTimeOffset(2026, 10, 13, 10, 0, 5, TimeSpan.Zero)).Should().Be("12:00:05");
        SimulatorText.DateAndTime(new DateTimeOffset(2026, 10, 13, 10, 0, 5, TimeSpan.Zero)).Should().Be("13-10-2026 12:00");
    }

    [Fact]
    public void ConsoleLinks_ACorrelationId_OpensItsTrailInTheConsolesSearchOverEverythingKept()
    {
        new ConsoleLinks(new Uri("https://console.example/")).Trail("a b").Should().Be("https://console.example/?key=a%20b&within=all");
    }

    [Fact]
    public void ConsoleLinks_WithoutAnAddress_HasNoLink()
    {
        new ConsoleLinks(null).Trail("corr").Should().BeNull();
    }
}

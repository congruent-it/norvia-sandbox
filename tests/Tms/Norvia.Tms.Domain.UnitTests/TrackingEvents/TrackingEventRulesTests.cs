using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.UnitTests.TrackingEvents;

public sealed class TrackingEventRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private readonly TrackingEventRules _rules = new(TimeSpan.FromMinutes(5));

    [Fact]
    public void CheckEventTime_InThePast_Passes() =>
        _rules.CheckEventTime(Now.AddHours(-1), Now).Should().BeNull();

    [Fact]
    public void CheckEventTime_AheadWithinTheClockSkew_Passes() =>
        _rules.CheckEventTime(Now.AddMinutes(5), Now).Should().BeNull();

    [Fact]
    public void CheckEventTime_AheadBeyondTheClockSkew_IsRefused_WithEventTimeInTheFuture() =>
        _rules.CheckEventTime(Now.AddMinutes(5).AddSeconds(1), Now)!.Code.Should().Be(TrackingEventRefusalCode.EventTimeInTheFuture);

    [Fact]
    public void CheckEventTime_ComparesInstants_NotLocalClockTimes() =>
        _rules.CheckEventTime(new DateTimeOffset(2026, 9, 25, 11, 30, 0, TimeSpan.FromHours(2)), Now).Should().BeNull("11:30+02:00 is 09:30 UTC");

    [Fact]
    public void ShipmentNotFound_NamesTheReferenceItDidNotFind() =>
        TrackingEventRules.ShipmentNotFound("VDT-9").Should().Match<TrackingEventRefusal>(
            refusal => refusal.Code == TrackingEventRefusalCode.ShipmentNotFound && refusal.Message.Contains("VDT-9", StringComparison.Ordinal));
}

using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

/// <summary>P4's overdue rule: a Requested booking's flag for the Planner's list of bookings awaiting an answer.</summary>
public sealed class CarrierBookingRulesTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
    private readonly CarrierBookingRules _rules = new(TimeSpan.FromHours(2));

    [Fact]
    public void IsOverdue_RequestedLessThanTheSettingAgo_WithNoCollectionWindowPassed_IsFalse()
    {
        _rules.IsOverdue(RequestedAt, RequestedAt.AddHours(6), RequestedAt.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void IsOverdue_RequestedAtLeastTheSettingAgo_IsTrue()
    {
        _rules.IsOverdue(RequestedAt, RequestedAt.AddDays(1), RequestedAt.AddHours(2)).Should().BeTrue();
    }

    [Fact]
    public void IsOverdue_TheCollectionWindowsEarliestTimeHasPassed_IsTrueEvenBeforeTheSetting()
    {
        _rules.IsOverdue(RequestedAt, RequestedAt.AddMinutes(30), RequestedAt.AddMinutes(31)).Should().BeTrue();
    }

    [Fact]
    public void IsOverdue_NoCollectionWindow_OnlyTheSettingMatters()
    {
        _rules.IsOverdue(RequestedAt, collectionWindowEarliest: null, RequestedAt.AddMinutes(30)).Should().BeFalse();
        _rules.IsOverdue(RequestedAt, collectionWindowEarliest: null, RequestedAt.AddHours(3)).Should().BeTrue();
    }
}

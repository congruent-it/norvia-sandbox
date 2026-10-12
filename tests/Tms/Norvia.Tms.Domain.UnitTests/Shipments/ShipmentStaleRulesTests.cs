using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>P5's Stale rules: a Shipment on its way whose next expected Tracking event hasn't arrived in time.</summary>
public sealed class ShipmentStaleRulesTests
{
    private static readonly DateTimeOffset Window = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly ShipmentStaleRules _rules = new(TimeSpan.FromHours(2), TimeSpan.FromHours(24), TimeSpan.FromHours(2));

    [Fact]
    public void NotCollected_JustBeforeTheThresholdAfterTheCollectionWindow_IsNotStale()
    {
        _rules.IsStale(ShipmentStatus.Booked, Window, null, null, Window.AddHours(2).AddTicks(-1)).Should().BeFalse();
    }

    [Fact]
    public void NotCollected_AtTheThresholdAfterTheCollectionWindow_IsStale()
    {
        _rules.IsStale(ShipmentStatus.Booked, Window, null, null, Window.AddHours(2)).Should().BeTrue();
    }

    [Fact]
    public void NotCollected_ADispatchedShipmentFollowsTheSameCollectionRuleAsABookedOne()
    {
        _rules.IsStale(ShipmentStatus.Dispatched, Window, null, null, Window.AddHours(2).AddTicks(-1)).Should().BeFalse();
        _rules.IsStale(ShipmentStatus.Dispatched, Window, null, null, Window.AddHours(2)).Should().BeTrue();
    }

    [Fact]
    public void NotCollected_ADispatchedShipmentIsNotStaleForSilence()
    {
        _rules.IsStale(ShipmentStatus.Dispatched, null, null, Window, Window.AddDays(10)).Should().BeFalse();
    }

    [Fact]
    public void NotCollected_WithoutACollectionWindow_IsNotStaleForIt()
    {
        _rules.IsStale(ShipmentStatus.Booked, null, null, null, Window.AddDays(30)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShipmentStatus.Collected)]
    [InlineData(ShipmentStatus.InTransit)]
    public void OnItsWay_SilentForLessThanTheLimit_IsNotStale(ShipmentStatus status)
    {
        _rules.IsStale(status, Window, null, Window.AddHours(1), Window.AddHours(1).AddHours(24).AddTicks(-1)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShipmentStatus.Collected)]
    [InlineData(ShipmentStatus.InTransit)]
    public void OnItsWay_SilentForTheLimit_IsStale(ShipmentStatus status)
    {
        _rules.IsStale(status, Window, null, Window.AddHours(1), Window.AddHours(25)).Should().BeTrue();
    }

    [Fact]
    public void OnItsWay_ANewEventResetsTheSilence()
    {
        var now = Window.AddHours(25);
        _rules.IsStale(ShipmentStatus.InTransit, null, null, Window.AddHours(1), now).Should().BeTrue();
        _rules.IsStale(ShipmentStatus.InTransit, null, null, now, now).Should().BeFalse();
    }

    [Fact]
    public void NotYetCollected_SilenceDoesNotMatter()
    {
        _rules.IsStale(ShipmentStatus.Booked, null, null, Window, Window.AddDays(10)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShipmentStatus.Booked)]
    [InlineData(ShipmentStatus.Dispatched)]
    [InlineData(ShipmentStatus.Collected)]
    [InlineData(ShipmentStatus.InTransit)]
    public void NotDelivered_AtTheThresholdAfterTheDeliveryWindow_IsStale(ShipmentStatus status)
    {
        var lastEvent = Window.AddHours(2);
        _rules.IsStale(status, null, Window, lastEvent, Window.AddHours(1).AddTicks(-1)).Should().BeFalse();
        _rules.IsStale(status, null, Window, lastEvent, Window.AddHours(2)).Should().BeTrue();
    }

    [Fact]
    public void Delivered_IsNeverStale()
    {
        _rules.IsStale(ShipmentStatus.Delivered, Window.AddDays(-9), Window.AddDays(-9), Window.AddDays(-9), Window).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShipmentStatus.Planned)]
    [InlineData(ShipmentStatus.Released)]
    public void NotYetBooked_IsNeverStale(ShipmentStatus status)
    {
        _rules.IsStale(status, Window.AddDays(-9), Window.AddDays(-9), null, Window).Should().BeFalse();
    }

    [Fact]
    public void ACollectedShipmentIsNoLongerStaleForNotBeingCollected()
    {
        var now = Window.AddHours(3);
        _rules.IsStale(ShipmentStatus.Booked, Window, null, null, now).Should().BeTrue();
        _rules.IsStale(ShipmentStatus.Collected, Window, null, now, now).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShipmentStatus.Collected)]
    [InlineData(ShipmentStatus.InTransit)]
    public void AwaitingRedelivery_IsNeverStale_ForSilenceOrForAPassedDeliveryWindow(ShipmentStatus status)
    {
        var now = Window.AddDays(10);

        _rules.IsStale(status, null, Window, Window, now).Should().BeTrue();
        _rules.IsStale(status, null, Window, Window, now, awaitingRedelivery: true).Should().BeFalse();
    }
}

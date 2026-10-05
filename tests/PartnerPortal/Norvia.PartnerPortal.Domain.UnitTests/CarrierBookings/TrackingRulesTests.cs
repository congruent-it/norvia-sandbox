using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class TrackingRulesTests
{
    [Theory]
    [InlineData(null, new[] { TrackingEventType.Collected, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Collected, new[] { TrackingEventType.Departed, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Departed, new[] { TrackingEventType.Arrived, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Arrived, new[] { TrackingEventType.OutForDelivery, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.OutForDelivery, new[] { TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Delivered, new TrackingEventType[0])]
    public void NextProgress_IsTheNextStep_AndDeliveredAtAnyTime(TrackingEventType? last, TrackingEventType[] expected) =>
        TrackingRules.NextProgress(last).Should().Equal(expected);

    [Fact]
    public void Exceptions_CollectionFailed_IsOfferedOnlyWhileBooked()
    {
        TrackingRules.Exceptions(null).Should().Equal(TrackingEventType.CollectionFailed, TrackingEventType.DeliveryFailed, TrackingEventType.Damaged);
        TrackingRules.Exceptions(TrackingEventType.Collected).Should().Equal(TrackingEventType.DeliveryFailed, TrackingEventType.Damaged);
        TrackingRules.Exceptions(TrackingEventType.Arrived).Should().Equal(TrackingEventType.DeliveryFailed, TrackingEventType.Damaged);
        TrackingRules.Exceptions(TrackingEventType.Delivered).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, ShipmentStage.Booked)]
    [InlineData(TrackingEventType.Collected, ShipmentStage.Collected)]
    [InlineData(TrackingEventType.Departed, ShipmentStage.InTransit)]
    [InlineData(TrackingEventType.Arrived, ShipmentStage.InTransit)]
    [InlineData(TrackingEventType.OutForDelivery, ShipmentStage.InTransit)]
    [InlineData(TrackingEventType.Delivered, ShipmentStage.Delivered)]
    public void StageOf_FollowsTheLastProgressEvent(TrackingEventType? last, ShipmentStage expected) =>
        TrackingRules.StageOf(last).Should().Be(expected);

    [Theory]
    [InlineData(TrackingEventType.CollectionFailed)]
    [InlineData(TrackingEventType.DeliveryFailed)]
    [InlineData(TrackingEventType.Damaged)]
    public void StageAfter_AnException_IsNone(TrackingEventType exception) => TrackingRules.StageAfter(exception).Should().BeNull();
}

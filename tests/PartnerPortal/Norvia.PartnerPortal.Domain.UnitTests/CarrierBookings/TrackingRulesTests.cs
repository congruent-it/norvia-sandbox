using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class TrackingRulesTests
{
    [Theory]
    [InlineData(null, new[] { TrackingEventType.Collected, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Collected, new[] { TrackingEventType.Departed, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Departed, new[] { TrackingEventType.Arrived, TrackingEventType.OutForDelivery, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Arrived, new[] { TrackingEventType.OutForDelivery, TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.OutForDelivery, new[] { TrackingEventType.Delivered })]
    [InlineData(TrackingEventType.Delivered, new TrackingEventType[0])]
    public void NextProgress_IsTheNextStep_OutForDeliveryInPlaceOfArrived_AndDeliveredAtAnyTime(TrackingEventType? last, TrackingEventType[] expected) =>
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

    [Theory]
    [InlineData(null, TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Collected, TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Departed, TrackingEventType.OutForDelivery)]
    [InlineData(TrackingEventType.Arrived, TrackingEventType.OutForDelivery)]
    [InlineData(TrackingEventType.OutForDelivery, TrackingEventType.Delivered)]
    public void NextAutomatic_IsTheNextOfCollectedDepartedOutForDeliveryAndDelivered(TrackingEventType? last, TrackingEventType expected) =>
        TrackingRules.NextAutomatic(last).Should().Be(expected);

    [Fact]
    public void NextAutomatic_OnceDelivered_IsNone() => TrackingRules.NextAutomatic(TrackingEventType.Delivered).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Arrived)]
    [InlineData(TrackingEventType.OutForDelivery)]
    public void NextAutomatic_IsAlwaysOfferedNext(TrackingEventType? last) =>
        TrackingRules.IsOffered(last, TrackingRules.NextAutomatic(last)!.Value).Should().BeTrue();
}

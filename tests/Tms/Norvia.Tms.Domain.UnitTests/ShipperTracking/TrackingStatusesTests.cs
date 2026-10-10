using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Domain.UnitTests.ShipperTracking;

public sealed class TrackingStatusesTests
{
    [Fact]
    public void Of_NoShipmentYet_IsReceived() =>
        TrackingStatuses.Of(null).Should().Be(TrackingStatus.Received);

    [Theory]
    [InlineData(ShipmentStatus.Planned, TrackingStatus.Scheduled)]
    [InlineData(ShipmentStatus.Released, TrackingStatus.Scheduled)]
    [InlineData(ShipmentStatus.Booked, TrackingStatus.Scheduled)]
    [InlineData(ShipmentStatus.Dispatched, TrackingStatus.Scheduled)]
    [InlineData(ShipmentStatus.Collected, TrackingStatus.Collected)]
    [InlineData(ShipmentStatus.InTransit, TrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivered, TrackingStatus.Delivered)]
    public void Of_AShipmentStatus_IsTheTrackingStatusTheShipperSees(ShipmentStatus shipment, TrackingStatus expected) =>
        TrackingStatuses.Of(shipment).Should().Be(expected);
}

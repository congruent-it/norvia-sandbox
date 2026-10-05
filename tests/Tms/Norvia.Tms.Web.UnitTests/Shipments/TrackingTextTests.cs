using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Web.Shipments;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class TrackingTextTests
{
    [Theory]
    [InlineData(TrackingEventType.Collected, "Collected")]
    [InlineData(TrackingEventType.OutForDelivery, "Out for delivery")]
    [InlineData(TrackingEventType.CollectionFailed, "Collection failed")]
    [InlineData(TrackingEventType.DeliveryFailed, "Delivery failed")]
    public void Type_IsReadableAsAPlannerSaysIt(TrackingEventType type, string expected)
    {
        TrackingText.Type(type).Should().Be(expected);
    }

    [Theory]
    [InlineData(ShipmentStatus.Booked, "Booked")]
    [InlineData(ShipmentStatus.InTransit, "In transit")]
    public void Status_IsTheGlossarysName(ShipmentStatus status, string expected)
    {
        TrackingText.Status(status).Should().Be(expected);
    }

    [Fact]
    public void Place_LeavesOutWhatIsNotGiven()
    {
        TrackingText.Place("Rotterdam", " ", "NL").Should().Be("Rotterdam, NL");
    }

    [Fact]
    public void Last_AShipmentWithNoTrackingEvent_SaysNoneYet()
    {
        TrackingText.Last(null).Should().Be("None yet");
    }

    [Fact]
    public void Last_ATrackingEvent_IsItsTypeTimeAndPlace()
    {
        var last = new LastTrackingEventView(TrackingEventType.Collected, new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero), "Rotterdam", "3011 AA", "NL");

        TrackingText.Last(last).Should().Be("Collected, Sat 26 Sep 2026, 06:00, Rotterdam, 3011 AA, NL");
    }
}

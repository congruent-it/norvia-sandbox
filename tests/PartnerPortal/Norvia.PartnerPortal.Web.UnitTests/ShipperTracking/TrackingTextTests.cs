using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.ShipperTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperTracking;

public sealed class TrackingTextTests
{
    [Fact]
    public void Status_NamesEveryTrackingStatusUniquely() =>
        Enum.GetValues<ShipperTrackingStatus>().Select(TrackingText.Status).Should().Equal("Received", "Scheduled", "Collected", "In transit", "Delivered");

    [Fact]
    public void Progress_NamesEveryStepUniquely() =>
        Enum.GetValues<ShipperProgressType>().Select(TrackingText.Progress).Should().Equal("Collected", "Departed", "Arrived", "Out for delivery", "Delivered");

    [Fact]
    public void Condition_NamesEveryCondition() =>
        Enum.GetValues<ProofOfDeliveryCondition>().Select(TrackingText.Condition).Should().Equal("Delivered clean", "Delivered with reservations");

    [Fact]
    public void Words_ForAValueTheyDoNotKnow_Throw()
    {
        ((Func<string>)(() => TrackingText.Status((ShipperTrackingStatus)99))).Should().Throw<ArgumentOutOfRangeException>();
        ((Func<string>)(() => TrackingText.Progress((ShipperProgressType)99))).Should().Throw<ArgumentOutOfRangeException>();
        ((Func<string>)(() => TrackingText.Condition((ProofOfDeliveryCondition)99))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Time_IsAsTheApiReturnedIt_OrADashForNone()
    {
        TrackingText.Time("02-10-2030 10:05").Should().Be("02-10-2030 10:05");
        TrackingText.Time(null).Should().Be("–");
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 kB")]
    [InlineData(48213, "47.1 kB")]
    [InlineData(1_523_004, "1.5 MB")]
    public void FileSize_IsInTheLargestFittingUnit(long bytes, string text) => TrackingText.FileSize(bytes).Should().Be(text);
}

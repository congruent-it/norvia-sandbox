using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class BookingReferencePatternTests
{
    private static readonly DateTimeOffset At = new(2024, 9, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_IsThePrefix_TheDay_AndTheBookingNumbersSequence() =>
        BookingReferencePattern.Generate("VDT", At, "CB-0000042").Should().Be("VDT-240917-0042");

    [Fact]
    public void Generate_KeepsTheLastFourDigitsOfALongerSequence() =>
        BookingReferencePattern.Generate("KES", At, "CB-0012345").Should().Be("KES-240917-2345");

    [Fact]
    public void Generate_WithoutDigitsInTheNumber_UsesZeros() =>
        BookingReferencePattern.Generate("PKT", At, "CB-X").Should().Be("PKT-240917-0000");
}

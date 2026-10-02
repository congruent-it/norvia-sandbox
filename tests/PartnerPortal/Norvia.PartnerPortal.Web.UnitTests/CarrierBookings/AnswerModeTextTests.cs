using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

public sealed class AnswerModeTextTests
{
    private static readonly DateTimeOffset Due = new(2026, 10, 2, 9, 0, 5, TimeSpan.Zero);

    private static CarrierBookingRequestView Request(CarrierBookingRequestStatus status, CarrierAnswerMode? mode, DateTimeOffset? dueAt) => new(
        Guid.CreateVersion7(), "CB-0000001", "SH-0000042", Due,
        new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", null, null, null), new CarrierBookingStop(null, null, "Utrecht", null, null, null), null, []),
        status, AutoAnswerMode: mode, AutoAnswerDueAt: dueAt);

    [Theory]
    [InlineData(CarrierAnswerMode.Manual, "Manual")]
    [InlineData(CarrierAnswerMode.AutoConfirm, "Auto-confirm")]
    [InlineData(CarrierAnswerMode.AutoReject, "Auto-reject")]
    public void Of_HasWordsForEveryMode(CarrierAnswerMode mode, string words) =>
        AnswerModeText.Of(mode).Should().Be(words);

    [Fact]
    public void Of_AnUnknownMode_Throws() =>
        FluentActions.Invoking(() => AnswerModeText.Of((CarrierAnswerMode)9)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Waiting_AnOpenRequestToConfirm_SaysWhenInRotterdamsTime() =>
        AnswerModeText.Waiting(Request(CarrierBookingRequestStatus.Open, CarrierAnswerMode.AutoConfirm, Due)).Should().Be("Auto-confirms at 11:00:05");

    [Fact]
    public void Waiting_AnOpenRequestToReject_SaysSo() =>
        AnswerModeText.Waiting(Request(CarrierBookingRequestStatus.Open, CarrierAnswerMode.AutoReject, Due)).Should().Be("Auto-rejects at 11:00:05");

    [Fact]
    public void Waiting_ARequestAnsweredByHand_SaysNothing() =>
        AnswerModeText.Waiting(Request(CarrierBookingRequestStatus.Open, null, null)).Should().BeNull();

    [Fact]
    public void Waiting_AClosedRequest_SaysNothing() =>
        AnswerModeText.Waiting(Request(CarrierBookingRequestStatus.Confirmed, CarrierAnswerMode.AutoConfirm, null)).Should().BeNull();
}

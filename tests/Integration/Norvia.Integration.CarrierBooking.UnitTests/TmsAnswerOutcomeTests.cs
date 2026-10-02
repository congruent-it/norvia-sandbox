namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class TmsAnswerOutcomeTests
{
    [Theory]
    [InlineData(200, "Succeeded", null)]
    [InlineData(403, "Refused", "CarrierNotAllowed")]
    [InlineData(404, "Refused", "BookingNotFound")]
    [InlineData(409, "Refused", "BookingNotOpen")]
    [InlineData(400, "Failed", "TmsCallRefused")]
    [InlineData(422, "Failed", "TmsCallRefused")]
    [InlineData(408, "Failed", "TmsUnavailable")]
    [InlineData(429, "Failed", "TmsUnavailable")]
    [InlineData(500, "Failed", "TmsUnavailable")]
    [InlineData(0, "Failed", "TmsUnavailable")]
    public void Of_AStatusOfTheTms_HasItsOutcomeAndFailureCode(int status, string outcome, string? failureCode) =>
        TmsAnswerOutcome.Of(status).Should().Be((outcome, failureCode));
}

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class ErpInvoiceOutcomeTests
{
    [Theory]
    [InlineData(200, "Succeeded", null)]
    [InlineData(201, "Succeeded", null)]
    [InlineData(400, "Refused", "CarrierInvoiceRefused")]
    [InlineData(409, "Refused", "InvoiceNumberUsed")]
    [InlineData(422, "Refused", "CarrierNotFound")]
    [InlineData(401, "Failed", "ErpCallRefused")]
    [InlineData(403, "Failed", "ErpCallRefused")]
    [InlineData(408, "Failed", "ErpUnavailable")]
    [InlineData(429, "Failed", "ErpUnavailable")]
    [InlineData(500, "Failed", "ErpUnavailable")]
    [InlineData(0, "Failed", "ErpUnavailable")]
    public void Of_AStatusOfTheErp_HasItsOutcomeAndFailureCode(int status, string outcome, string? failureCode) =>
        ErpInvoiceOutcome.Of(status).Should().Be((outcome, failureCode));
}

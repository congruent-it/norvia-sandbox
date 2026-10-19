using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceTrackingTests
{
    [Fact]
    public void KeysOf_AreTheTrackingContractsKeys_WithTheInvoiceNumberAsTheDocumentNumber() =>
        CarrierInvoiceTracking.KeysOf(Samples.VanDijkTransport, "INV-1", "corr-1").Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["flow"] = "carrier-invoice",
            ["partyId"] = "CAR-0001",
            ["documentNumber"] = "INV-1",
            ["correlationId"] = "corr-1",
        });

    [Fact]
    public void KeysOf_WhenNorviaNeverReplied_LeavesTheCorrelationIdOut() =>
        CarrierInvoiceTracking.KeysOf(Samples.VanDijkTransport, "INV-1", correlationId: null).Should().NotContainKey("correlationId");

    [Theory]
    [InlineData(CarrierInvoiceReplyResult.Received, "Succeeded", null)]
    [InlineData(CarrierInvoiceReplyResult.InvoiceNumberUsed, "Refused", "InvoiceNumberUsed")]
    [InlineData(CarrierInvoiceReplyResult.CarrierNotFound, "Refused", "CarrierNotFound")]
    [InlineData(CarrierInvoiceReplyResult.Invalid, "Refused", "CarrierInvoiceUnreadable")]
    [InlineData(CarrierInvoiceReplyResult.Refused, "Refused", null)]
    [InlineData(CarrierInvoiceReplyResult.NotReceived, "Failed", "CarrierInvoiceNotReceived")]
    public void OutcomeOf_IsTheOutcomeAndFailureCodeOfTheReply(CarrierInvoiceReplyResult result, string outcome, string? failureCode)
    {
        var keys = CarrierInvoiceTracking.OutcomeOf(result);

        keys["outcome"].Should().Be(outcome);
        if (failureCode is null)
        {
            keys.Should().NotContainKey("failureCode");
        }
        else
        {
            keys["failureCode"].Should().Be(failureCode);
        }
    }

    [Fact]
    public void OutcomeOf_AnUnknownResult_Throws() =>
        FluentActions.Invoking(() => CarrierInvoiceTracking.OutcomeOf((CarrierInvoiceReplyResult)99)).Should().Throw<ArgumentOutOfRangeException>();
}

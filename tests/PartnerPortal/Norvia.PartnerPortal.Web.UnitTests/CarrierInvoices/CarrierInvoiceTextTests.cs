using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Web.CarrierInvoices;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceTextTests
{
    private static SubmittedCarrierInvoice Sent(CarrierInvoiceReply reply) => new("INV-1", reply, Invoice: null);

    private static CarrierInvoiceReply Received(CarrierInvoiceStatus status, decimal? approved = null) =>
        CarrierInvoiceReply.Received("corr-1", new CarrierInvoiceReceipt(false, status, approved, [new("VDT-1", 10m, CarrierInvoiceLineResult.Matched)]));

    [Theory]
    [InlineData(CarrierInvoiceLineResult.Matched, "Matched")]
    [InlineData(CarrierInvoiceLineResult.PriceMismatch, "Price mismatch")]
    [InlineData(CarrierInvoiceLineResult.UnknownBooking, "Unknown booking")]
    [InlineData(CarrierInvoiceLineResult.Duplicate, "Duplicate")]
    public void Of_NamesWhatNorviaMadeOfALine(CarrierInvoiceLineResult result, string text) =>
        CarrierInvoiceText.Of(result).Should().Be(text);

    [Theory]
    [InlineData(CarrierInvoiceOutcomeResult.Matched, "Matched")]
    [InlineData(CarrierInvoiceOutcomeResult.Accepted, "Accepted")]
    [InlineData(CarrierInvoiceOutcomeResult.AcceptedAdjusted, "Accepted, adjusted")]
    [InlineData(CarrierInvoiceOutcomeResult.Rejected, "Rejected")]
    public void Of_NamesWhatBecameOfALine(CarrierInvoiceOutcomeResult result, string text) =>
        CarrierInvoiceText.Of(result).Should().Be(text);

    [Theory]
    [InlineData(CarrierInvoiceStatus.Received, "Received")]
    [InlineData(CarrierInvoiceStatus.Approved, "Approved")]
    public void Of_NamesTheStatus(CarrierInvoiceStatus status, string text) =>
        CarrierInvoiceText.Of(status).Should().Be(text);

    [Fact]
    public void Of_ValuesTheScreensHaveNoWordsFor_Throw()
    {
        var line = () => CarrierInvoiceText.Of((CarrierInvoiceLineResult)99);
        var outcome = () => CarrierInvoiceText.Of((CarrierInvoiceOutcomeResult)99);
        var status = () => CarrierInvoiceText.Of((CarrierInvoiceStatus)99);

        line.Should().Throw<ArgumentOutOfRangeException>();
        outcome.Should().Throw<ArgumentOutOfRangeException>();
        status.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Outcome_SaysWaitingUntilNorviaTells_ThenTheResultWithTheAcceptedAmount()
    {
        CarrierInvoiceText.Outcome(null).Should().Be("Waiting for Norvia");
        CarrierInvoiceText.Outcome(new CarrierInvoiceOutcomeLine("VDT-1", CarrierInvoiceOutcomeResult.AcceptedAdjusted, 90m, "x")).Should().Be("Accepted, adjusted at € 90.00");
        CarrierInvoiceText.Outcome(new CarrierInvoiceOutcomeLine("VDT-1", CarrierInvoiceOutcomeResult.Rejected, 0m, "x")).Should().Be("Rejected");
    }

    [Fact]
    public void Amount_IsWrittenWithTwoDecimals_AndReadBack()
    {
        CarrierInvoiceText.Amount(1250m).Should().Be("1250.00");
        CarrierInvoiceText.Amount(null).Should().BeEmpty();
        CarrierInvoiceText.TryParseAmount(" 1250.5 ", out var amount).Should().BeTrue();
        amount.Should().Be(1250.5m);
        CarrierInvoiceText.TryParseAmount("twelve", out _).Should().BeFalse();
        CarrierInvoiceText.TryParseAmount(null, out _).Should().BeFalse();
    }

    [Fact]
    public void NoticeOf_AnInvoiceApprovedOnReceipt_SaysSo_WithTheApprovedAmount()
    {
        var notice = CarrierInvoiceText.NoticeOf(Sent(Received(CarrierInvoiceStatus.Approved, 1234.50m)));

        notice.Intent.Should().Be(MessageIntent.Success);
        notice.Text.Should().Contain("invoice INV-1").And.Contain("approved it at € 1,234.50");
        notice.MessageId.Should().Be("corr-1");
    }

    [Fact]
    public void NoticeOf_AnInvoiceWaitingForFinance_SaysNorviaTellsTheOutcomeLater()
    {
        var notice = CarrierInvoiceText.NoticeOf(Sent(Received(CarrierInvoiceStatus.Received)));

        notice.Intent.Should().Be(MessageIntent.Info);
        notice.Text.Should().Contain("waits for Norvia's Finance");
    }

    [Fact]
    public void NoticeOf_ADuplicateNumber_IsAnError_InNorviasWords()
    {
        var notice = CarrierInvoiceText.NoticeOf(Sent(CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.InvoiceNumberUsed, "Use a new number.", "corr-2")));

        notice.Intent.Should().Be(MessageIntent.Error);
        notice.Text.Should().Be("Use a new number.");
    }

    [Fact]
    public void NoticeOf_AnInvoiceNotReceived_WarnsToSendItAgain()
    {
        var notice = CarrierInvoiceText.NoticeOf(Sent(CarrierInvoiceReply.NotReceived(null)));

        notice.Intent.Should().Be(MessageIntent.Warning);
        notice.Text.Should().Contain("did not receive").And.Contain("Send it again");
        notice.MessageId.Should().BeNull();
    }

    [Fact]
    public void NoticeOf_ARefusal_IsAnError()
    {
        var notice = CarrierInvoiceText.NoticeOf(Sent(CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.Refused, "No key.", null)));

        notice.Intent.Should().Be(MessageIntent.Error);
        notice.Text.Should().Be("No key.");
    }
}

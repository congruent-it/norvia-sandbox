namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>What the Norvia Partner API answered to a Carrier invoice (contracts/partner-api, Carrier invoice responses).</summary>
public enum CarrierInvoiceReplyResult
{
    /// <summary>Norvia recorded the invoice and matched each line.</summary>
    Received,

    /// <summary>The Carrier used this invoice number for different content.</summary>
    InvoiceNumberUsed,

    /// <summary>Norvia has no Carrier for the Persona.</summary>
    CarrierNotFound,

    /// <summary>Norvia can't use the invoice. Nothing was recorded.</summary>
    Invalid,

    /// <summary>The Norvia Partner API refused the Persona's key, token or role.</summary>
    Refused,

    /// <summary>Norvia couldn't take the invoice in, or couldn't be reached. Sending it again is safe.</summary>
    NotReceived,
}

/// <param name="Repeated">True when Norvia had the same invoice already.</param>
public sealed record CarrierInvoiceReceipt(
    bool Repeated,
    CarrierInvoiceStatus Status,
    decimal? ApprovedAmount,
    IReadOnlyList<CarrierInvoiceReceiptLine> Lines);

public sealed record CarrierInvoiceReceiptLine(string BookingRef, decimal Amount, CarrierInvoiceLineResult Result);

public sealed record CarrierInvoiceReply
{
    private CarrierInvoiceReply(CarrierInvoiceReplyResult result, string? messageId, string? text, CarrierInvoiceReceipt? receipt)
    {
        Result = result;
        MessageId = messageId;
        Text = text;
        Receipt = receipt;
    }

    public CarrierInvoiceReplyResult Result { get; }

    /// <summary>The correlation ID API Management gave the invoice, by which Norvia finds every trace of it. Null when Norvia never replied.</summary>
    public string? MessageId { get; }

    /// <summary>Norvia's own words for a refusal; null for a received invoice.</summary>
    public string? Text { get; }

    /// <summary>What Norvia made of the invoice; there only for <see cref="CarrierInvoiceReplyResult.Received"/>.</summary>
    public CarrierInvoiceReceipt? Receipt { get; }

    public static CarrierInvoiceReply Received(string? messageId, CarrierInvoiceReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new CarrierInvoiceReply(CarrierInvoiceReplyResult.Received, messageId, text: null, receipt);
    }

    public static CarrierInvoiceReply NotReceived(string? messageId) =>
        new(CarrierInvoiceReplyResult.NotReceived, messageId, text: null, receipt: null);

    public static CarrierInvoiceReply Refusal(CarrierInvoiceReplyResult result, string text, string? messageId)
    {
        if (result is CarrierInvoiceReplyResult.Received or CarrierInvoiceReplyResult.NotReceived)
        {
            throw new ArgumentOutOfRangeException(nameof(result), result, "A refusal is not a received or a lost invoice.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new CarrierInvoiceReply(result, messageId, text, receipt: null);
    }
}

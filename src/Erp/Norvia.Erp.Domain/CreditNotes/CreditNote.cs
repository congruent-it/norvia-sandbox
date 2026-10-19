using System.Globalization;
using Ardalis.SharedKernel;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>
/// Finance's correction of one line of an issued Shipper invoice, in full or in part, with a reason. Once issued it is never changed or
/// deleted, and the invoice it corrects is left as it was. Its delivery status follows the Shipper invoice's: Issued until the Shipper invoice
/// Logic App reports it Sent or Not delivered, and Issued once more when Finance sends it again.
/// </summary>
public sealed class CreditNote : EntityBase<CreditNoteNumber>, IAggregateRoot
{
    public const int MaxReasonLength = 500;

    private CreditNote()
    {
    }

    private CreditNote(
        CreditNoteNumber number,
        ShipperInvoice invoice,
        ShipperInvoiceLine line,
        decimal amountWithoutVat,
        string reason,
        DateTimeOffset issuedAt,
        string correlationId)
    {
        Id = number;
        ShipperId = invoice.ShipperId;
        InvoiceNumber = invoice.Id;
        TransportOrderNumber = line.TransportOrderNumber;
        ShipperReference = line.ShipperReference;
        ShipmentNumber = line.ShipmentNumber;
        DeliveryDate = DateOnly.FromDateTime(line.DeliveryTime.DateTime);
        CreditNoteDate = ShipperInvoice.InvoiceDateOf(issuedAt);
        AmountWithoutVat = amountWithoutVat;
        Vat = ShipperInvoice.VatOf(amountWithoutVat);
        TotalWithVat = AmountWithoutVat + Vat;
        Reason = reason;
        IssuedAt = issuedAt;
        CorrelationId = correlationId;
        Status = ShipperInvoiceStatus.Issued;
    }

    public ShipperId ShipperId { get; private set; }

    /// <summary>The Shipper invoice credited.</summary>
    public InvoiceNumber InvoiceNumber { get; private set; }

    /// <summary>The Transport Order number of the invoice line credited, which identifies the line within its invoice.</summary>
    public string TransportOrderNumber { get; private set; } = string.Empty;

    public string ShipperReference { get; private set; } = string.Empty;

    public string ShipmentNumber { get; private set; } = string.Empty;

    /// <summary>The day the credited line's Shipment was delivered, where it happened.</summary>
    public DateOnly DeliveryDate { get; private set; }

    /// <summary>The day the Credit note was issued, Europe/Amsterdam.</summary>
    public DateOnly CreditNoteDate { get; private set; }

    public decimal AmountWithoutVat { get; private set; }

    public decimal Vat { get; private set; }

    public decimal TotalWithVat { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>The correlation ID the ERP created for this Credit note; the event carries it on.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    public ShipperInvoiceStatus Status { get; private set; }

    /// <summary>When the Shipper invoice Logic App reported the Credit note Sent; null while it is Issued only.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When the Shipper invoice Logic App reported the Credit note Not delivered; null unless it is Not delivered now.</summary>
    public DateTimeOffset? NotDeliveredAt { get; private set; }

    /// <summary>The Shipper endpoint's HTTP status code of a Not delivered Credit note; null when the failure had none, such as a timeout.</summary>
    public int? NotDeliveredStatusCode { get; private set; }

    /// <summary>The technical cause of a Not delivered Credit note, in plain language.</summary>
    public string? NotDeliveredMessage { get; private set; }

    /// <summary>
    /// Records that the Shipper's endpoint acknowledged the Credit note. Only an Issued one can be reported; false for one reported
    /// already, which the caller ignores and logs.
    /// </summary>
    public bool ReportSent(DateTimeOffset at)
    {
        if (Status != ShipperInvoiceStatus.Issued)
        {
            return false;
        }

        Status = ShipperInvoiceStatus.Sent;
        SentAt = at;
        return true;
    }

    /// <summary>
    /// Records that the Credit note never reached the Shipper, with the technical cause. Only an Issued one can be reported; false for
    /// one reported already or sent again since, which the caller ignores and logs.
    /// </summary>
    public bool ReportNotDelivered(int? statusCode, string message, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (Status != ShipperInvoiceStatus.Issued)
        {
            return false;
        }

        Status = ShipperInvoiceStatus.NotDelivered;
        NotDeliveredAt = at;
        NotDeliveredStatusCode = statusCode;
        NotDeliveredMessage = message.Length > ShipperInvoice.MaxCauseLength ? message[..ShipperInvoice.MaxCauseLength] : message;
        return true;
    }

    /// <summary>
    /// Finance sends a Not delivered Credit note again: it is Issued once more, without a sent time, until the Logic App reports it. The
    /// Credit note itself is unchanged. False for one that isn't Not delivered.
    /// </summary>
    public bool SendAgain()
    {
        if (Status != ShipperInvoiceStatus.NotDelivered)
        {
            return false;
        }

        Status = ShipperInvoiceStatus.Issued;
        NotDeliveredAt = null;
        NotDeliveredStatusCode = null;
        NotDeliveredMessage = null;
        return true;
    }

    /// <summary>
    /// What stops a Credit note of <paramref name="amount"/> on <paramref name="transportOrderNumber"/>'s line of the invoice, or null when
    /// nothing does. <paramref name="alreadyCredited"/> is the sum of the Credit notes on that line so far.
    /// </summary>
    public static CreditNoteRefusal? Refuse(
        ShipperInvoice invoice,
        string transportOrderNumber,
        decimal amount,
        string? reason,
        decimal alreadyCredited)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Lines.All(line => line.TransportOrderNumber != transportOrderNumber))
        {
            return new(CreditNoteRefusalCode.InvoiceLineNotFound, $"Shipper invoice {invoice.Id.Value} has no line for Transport Order {transportOrderNumber}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return new(CreditNoteRefusalCode.CreditNoteReasonRequired, "A Credit note needs a reason.");
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            return new(CreditNoteRefusalCode.CreditNoteReasonTooLong, $"The reason may be at most {MaxReasonLength} characters.");
        }

        if (amount <= 0m || decimal.Round(amount, 2) != amount)
        {
            return new(CreditNoteRefusalCode.CreditNoteAmountInvalid, "The amount must be above zero, in whole cents.");
        }

        var line = invoice.Lines.Single(candidate => candidate.TransportOrderNumber == transportOrderNumber);
        var remaining = line.Total - alreadyCredited;
        return amount > remaining
            ? new(
                CreditNoteRefusalCode.CreditNoteAboveLineTotal,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The amount {amount:N2} is above what is left to credit on line {transportOrderNumber} of {invoice.Id.Value}: {remaining:N2} (the line total {line.Total:N2} minus {alreadyCredited:N2} credited already)."))
            : null;
    }

    /// <summary>
    /// Issues a Credit note. The caller has checked it with <see cref="Refuse"/> and saves it with the number's sequence and the event in one
    /// transaction.
    /// </summary>
    public static CreditNote Issue(
        CreditNoteNumber number,
        ShipperInvoice invoice,
        string transportOrderNumber,
        decimal amountWithoutVat,
        string reason,
        decimal alreadyCredited,
        DateTimeOffset issuedAt,
        string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (Refuse(invoice, transportOrderNumber, amountWithoutVat, reason, alreadyCredited) is { } refusal)
        {
            throw new ArgumentException(refusal.Message, nameof(amountWithoutVat));
        }

        var line = invoice.Lines.Single(candidate => candidate.TransportOrderNumber == transportOrderNumber);
        return new CreditNote(number, invoice, line, amountWithoutVat, reason.Trim(), issuedAt, correlationId);
    }
}

/// <summary>Why a Credit note can't be issued, with a stable code and a readable message.</summary>
public sealed record CreditNoteRefusal(CreditNoteRefusalCode Code, string Message);

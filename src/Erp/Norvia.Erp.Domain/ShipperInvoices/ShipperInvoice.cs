using Ardalis.SharedKernel;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>
/// The ERP's invoice to a Shipper over its Billable lines, one line per Transport Order. Once issued it is final: its number, lines and
/// totals never change and it is never deleted. Only its delivery status follows the Shipper invoice Logic App's reports. Corrections are
/// Credit notes.
/// </summary>
public sealed class ShipperInvoice : EntityBase<InvoiceNumber>, IAggregateRoot
{
    /// <summary>Dutch VAT rate; reverse charge and other Dutch VAT rules are deferred, as every seed Shipper is Dutch.</summary>
    public const decimal VatRate = 0.21m;

    public const int MaxCauseLength = 500;

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private ShipperInvoice()
    {
    }

    private ShipperInvoice(
        InvoiceNumber number,
        ShipperId shipperId,
        DateOnly invoiceDate,
        IReadOnlyList<ShipperInvoiceLine> lines,
        DateTimeOffset issuedAt,
        string correlationId)
    {
        Id = number;
        ShipperId = shipperId;
        InvoiceDate = invoiceDate;
        Lines = lines;
        TotalWithoutVat = lines.Sum(line => line.Total);
        Vat = VatOf(TotalWithoutVat);
        TotalWithVat = TotalWithoutVat + Vat;
        IssuedAt = issuedAt;
        CorrelationId = correlationId;
        Status = ShipperInvoiceStatus.Issued;
    }

    public ShipperId ShipperId { get; private set; }

    /// <summary>The date of the run, Europe/Amsterdam.</summary>
    public DateOnly InvoiceDate { get; private set; }

    public IReadOnlyList<ShipperInvoiceLine> Lines { get; private set; } = [];

    public decimal TotalWithoutVat { get; private set; }

    public decimal Vat { get; private set; }

    public decimal TotalWithVat { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>The correlation ID the ERP created for this invoice; the event and the Shipper invoice Logic App's call carry it on.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    public ShipperInvoiceStatus Status { get; private set; }

    /// <summary>When the Shipper invoice Logic App reported the invoice Sent; null while it is Issued only.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When the Shipper invoice Logic App reported the invoice Not delivered; null unless it is Not delivered now.</summary>
    public DateTimeOffset? NotDeliveredAt { get; private set; }

    /// <summary>The Shipper endpoint's HTTP status code of a Not delivered invoice; null when the failure had none, such as a timeout.</summary>
    public int? NotDeliveredStatusCode { get; private set; }

    /// <summary>The technical cause of a Not delivered invoice, in plain language.</summary>
    public string? NotDeliveredMessage { get; private set; }

    /// <summary>
    /// Records that the Shipper's endpoint acknowledged the invoice. Only an Issued invoice can be reported; false for one reported
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
    /// Records that the invoice never reached the Shipper, with the technical cause. Only an Issued invoice can be reported; false for
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
        NotDeliveredMessage = message.Length > MaxCauseLength ? message[..MaxCauseLength] : message;
        return true;
    }

    /// <summary>
    /// Finance sends a Not delivered invoice again: it is Issued once more, without a sent time, until the Logic App reports it. The
    /// invoice itself is unchanged. False for an invoice that isn't Not delivered.
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

    /// <summary>The calendar year of an Invoice run, which picks the sequence its number comes from.</summary>
    public static int InvoiceYear(DateTimeOffset runAt) => InvoiceDateOf(runAt).Year;

    /// <summary>The date of an Invoice run: the day it is in Amsterdam.</summary>
    public static DateOnly InvoiceDateOf(DateTimeOffset runAt) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(runAt, Amsterdam).DateTime);

    /// <summary>21% of an amount without VAT, rounded to cents, half away from zero.</summary>
    public static decimal VatOf(decimal totalWithoutVat) => Math.Round(totalWithoutVat * VatRate, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Issues one invoice over all the Waiting lines given, which must all be the Shipper's, and sets each of them to Invoiced with the
    /// invoice's number. The caller saves the invoice, the lines and the number's sequence in one transaction.
    /// </summary>
    public static ShipperInvoice Issue(
        InvoiceNumber number,
        ShipperId shipperId,
        IReadOnlyCollection<BillableLine> waitingLines,
        DateTimeOffset runAt,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(waitingLines);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (waitingLines.Count == 0)
        {
            throw new ArgumentException("A Shipper without Waiting lines gets no invoice.", nameof(waitingLines));
        }

        if (waitingLines.Any(line => line.ShipperId != shipperId))
        {
            throw new ArgumentException("An invoice has the lines of its Shipper only.", nameof(waitingLines));
        }

        var invoiceLines = waitingLines
            .OrderBy(line => line.DeliveryTime).ThenBy(line => line.Id)
            .Select(line =>
            {
                line.Invoice(number);
                return ShipperInvoiceLine.Of(line);
            })
            .ToList();

        return new ShipperInvoice(number, shipperId, InvoiceDateOf(runAt), invoiceLines, runAt, correlationId);
    }
}

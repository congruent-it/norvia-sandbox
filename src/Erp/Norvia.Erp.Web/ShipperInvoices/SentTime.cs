using System.Globalization;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>How the lists show when a Shipper invoice was reported Sent: the time in Amsterdam, or a dash for an invoice that wasn't.</summary>
internal static class SentTime
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>What the lists show for an invoice: the sent time, "Not delivered" for one that never reached the Shipper, or a dash.</summary>
    public static string Format(Norvia.Erp.Application.ShipperInvoices.ShipperInvoiceView invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return invoice.Status == nameof(Norvia.Erp.Domain.ShipperInvoices.ShipperInvoiceStatus.NotDelivered) ? "Not delivered" : Format(invoice.SentAt);
    }

    /// <summary>What the lists show for a Credit note: the sent time, "Not delivered" for one that never reached the Shipper, or a dash.</summary>
    public static string Format(Norvia.Erp.Application.CreditNotes.CreditNoteView creditNote)
    {
        ArgumentNullException.ThrowIfNull(creditNote);
        return creditNote.Status == nameof(Norvia.Erp.Domain.ShipperInvoices.ShipperInvoiceStatus.NotDelivered) ? "Not delivered" : Format(creditNote.SentAt);
    }

    public static string Format(DateTimeOffset? sentAt) =>
        sentAt is { } at
            ? TimeZoneInfo.ConvertTime(at, Amsterdam).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "—";
}

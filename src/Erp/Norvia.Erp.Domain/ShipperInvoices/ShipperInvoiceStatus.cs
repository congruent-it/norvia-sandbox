namespace Norvia.Erp.Domain.ShipperInvoices;

public enum ShipperInvoiceStatus
{
    /// <summary>The invoice is issued and final. It stays Issued, without a sent time, until the Shipper invoice Logic App reports it.</summary>
    Issued,

    /// <summary>The Shipper invoice Logic App reported that the Shipper's invoice endpoint acknowledged the invoice.</summary>
    Sent,

    /// <summary>The Shipper invoice Logic App reported that the invoice never reached the Shipper. Finance can send it again.</summary>
    NotDelivered,
}

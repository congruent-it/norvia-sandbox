namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>The stable codes of a refused report on a Shipper invoice. The names are part of the ERP API's contract.</summary>
public enum ShipperInvoiceRefusalCode
{
    ShipperInvoiceNotFound,

    /// <summary>Send again was chosen for an invoice that isn't Not delivered.</summary>
    ShipperInvoiceNotSendableAgain,
}

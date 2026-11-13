namespace Norvia.Erp.Application.IntegrationEvents;

/// <summary>Stable flow names of the tracking contract (coding guidelines §7).</summary>
public static class Flows
{
    /// <summary>
    /// Billing (P9, E1): the Billing Logic App hands the ERP a Billable Transport Order, on the correlation ID of the transaction that
    /// made it Billable.
    /// </summary>
    public const string Billing = "billing";

    /// <summary>Invoicing (E1): the ERP issues a Shipper invoice, on a correlation ID of its own.</summary>
    public const string Invoicing = "invoicing";

    /// <summary>
    /// Carrier costs (E2): the Carrier cost Logic App hands the ERP the Carrier cost of a Delivered Shipment, on the correlation ID of the
    /// Delivered Tracking event.
    /// </summary>
    public const string CarrierCosts = "carrier-costs";

    /// <summary>
    /// Carrier invoices (E2): a Carrier sends the ERP a Carrier invoice through the Carrier invoice Logic App, on a correlation ID API
    /// Management created for it, which the ERP keeps and <c>CarrierInvoiceApproved</c> carries.
    /// </summary>
    public const string CarrierInvoices = "carrier-invoices";

    /// <summary>Master-data sync (E3): Finance edits master data in the ERP, which replicates each change to the TMS, on a correlation ID of its own.</summary>
    public const string MasterData = "master-data";
}

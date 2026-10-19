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
}

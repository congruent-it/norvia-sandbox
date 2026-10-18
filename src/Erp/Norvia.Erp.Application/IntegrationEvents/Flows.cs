namespace Norvia.Erp.Application.IntegrationEvents;

/// <summary>Stable flow names of the tracking contract (coding guidelines §7).</summary>
public static class Flows
{
    /// <summary>
    /// Billing (P9, E1): the Billing Logic App hands the ERP a Billable Transport Order, on the correlation ID of the transaction that
    /// made it Billable.
    /// </summary>
    public const string Billing = "billing";
}

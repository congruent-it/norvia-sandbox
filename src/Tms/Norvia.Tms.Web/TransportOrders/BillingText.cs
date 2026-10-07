using Norvia.Tms.Application.Billing.ListNotYetBillable;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>Words why a Transport Order isn't Billable yet (P9) as Customer service reads it.</summary>
public static class BillingText
{
    public static string Reason(BillingBlockingReason reason) => reason switch
    {
        BillingBlockingReason.NotRated => "Not rated",
        BillingBlockingReason.NoProofOfDelivery => "No Proof of delivery",
        BillingBlockingReason.OpenException => "Open Exception",
        _ => reason.ToString(),
    };

    public static string State(BillingState state) => state switch
    {
        BillingState.NotYetBillable => "Not yet billable",
        BillingState.Billable => "Billable",
        BillingState.NotBilledSeparately => "Not billed separately (Fulfilment Contract)",
        _ => state.ToString(),
    };
}

using Norvia.Tms.Application.Billing.ListNotYetBillable;

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
}

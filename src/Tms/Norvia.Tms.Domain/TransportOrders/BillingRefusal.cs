using static System.FormattableString;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>Why the TMS refused Customer service's release of a Transport Order without a Proof of delivery: a stable code and a readable message.</summary>
public sealed record BillingRefusal(BillingRefusalCode Code, string Message)
{
    public static BillingRefusal NotFound(string transportOrderNumber) =>
        new(BillingRefusalCode.TransportOrderNotFound, Invariant($"The TMS has no Transport Order {transportOrderNumber}."));
}

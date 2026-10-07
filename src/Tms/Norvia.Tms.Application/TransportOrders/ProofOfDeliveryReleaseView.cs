using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <summary>Who released a Transport Order without a Proof of delivery (P9), when, and why.</summary>
public sealed record ProofOfDeliveryReleaseView(string ReleasedBy, DateTimeOffset ReleasedAt, string Remark)
{
    public static ProofOfDeliveryReleaseView? From(TransportOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order is { ReleasedWithoutProofOfDelivery: true, ReleasedBy: { } by, ReleasedAt: { } at, ReleaseRemark: { } remark }
            ? new(by, at, remark)
            : null;
    }
}

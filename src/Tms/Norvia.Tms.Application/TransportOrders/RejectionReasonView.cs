using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <param name="Code">The stable rejection code, such as <c>GoodsLinesMissing</c>.</param>
public sealed record RejectionReasonView(string Code, string Message)
{
    public static RejectionReasonView From(RejectionReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new(reason.Code.ToString(), reason.Message);
    }

    public static IReadOnlyList<RejectionReasonView> From(IEnumerable<RejectionReason> reasons) => [.. reasons.Select(From)];
}

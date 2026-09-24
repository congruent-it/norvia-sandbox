using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

/// <summary>
/// The outcome of a submission: an Accepted Transport Order with its number, or a Rejected booking with every reason.
/// A repeat of an earlier booking returns the original Transport Order, flagged as a repeat.
/// </summary>
public sealed record SubmitTransportOrderResult(
    Guid Id,
    TransportOrderStatus Status,
    string? TransportOrderNumber,
    IReadOnlyList<RejectionReasonView> RejectionReasons,
    bool Repeat = false)
{
    public static SubmitTransportOrderResult From(TransportOrder order, bool repeat = false)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Id.Value, order.Status, order.Number?.Value, RejectionReasonView.From(order.RejectionReasons), repeat);
    }
}

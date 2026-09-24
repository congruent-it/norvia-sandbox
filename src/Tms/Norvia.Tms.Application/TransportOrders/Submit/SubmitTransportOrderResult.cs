using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

/// <summary>The outcome of a submission: an Accepted Transport Order with its number, or a Rejected booking with every reason.</summary>
public sealed record SubmitTransportOrderResult(
    Guid Id,
    TransportOrderStatus Status,
    string? TransportOrderNumber,
    IReadOnlyList<RejectionReasonView> RejectionReasons);

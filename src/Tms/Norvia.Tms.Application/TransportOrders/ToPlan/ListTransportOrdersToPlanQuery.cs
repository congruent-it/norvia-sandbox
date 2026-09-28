using Mediator;

namespace Norvia.Tms.Application.TransportOrders.ToPlan;

/// <summary>
/// The Planner's Transport Orders to plan: Accepted and not yet Planned, the earliest collection first. The rating result
/// is shown with each, and never filters one out.
/// </summary>
public sealed record ListTransportOrdersToPlanQuery : IQuery<IReadOnlyList<TransportOrderToPlanSummary>>;

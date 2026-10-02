using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.RatingExceptions;
using Norvia.Tms.Application.TransportOrders.ToPlan;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>What the TMS UI reads about Transport Orders and their Shipments, all of it through the TMS query side.</summary>
public interface ITransportOrderQueries
{
    /// <summary>The Transport Orders waiting for planning: Accepted and not yet Planned, the earliest collection first.</summary>
    Task<IReadOnlyList<TransportOrderToPlanSummary>> ListWaitingForPlanningAsync(CancellationToken cancellationToken);

    /// <summary>The Transport Order with this number, or null when there is none.</summary>
    Task<TransportOrderView?> FindAsync(string transportOrderNumber, CancellationToken cancellationToken);

    /// <summary>The Rejected bookings, which Customer service looks up when a Shipper calls about one.</summary>
    Task<IReadOnlyList<TransportOrderSummary>> ListRejectedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The Rejected booking with this ID, or null when there is none. A Rejected booking has no Transport Order number,
    /// so it is found by its ID.
    /// </summary>
    Task<TransportOrderView?> FindRejectedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The rating exceptions: the Transport Orders whose Rating failed, which Customer service follows up.</summary>
    Task<IReadOnlyList<RatingExceptionSummary>> ListRatingExceptionsAsync(CancellationToken cancellationToken);

    /// <summary>The Carriers a Planner may assign to: only the active ones, by name.</summary>
    Task<IReadOnlyList<CarrierView>> ListActiveCarriersAsync(CancellationToken cancellationToken);

    /// <summary>The Shipment with this number, or null when there is none.</summary>
    Task<ShipmentView?> FindShipmentAsync(string shipmentNumber, CancellationToken cancellationToken);

    /// <summary>The Planner's Shipments to release: the Planned Shipments, with who carries each, in the order they were planned.</summary>
    Task<IReadOnlyList<ShipmentToReleaseSummary>> ListShipmentsToReleaseAsync(CancellationToken cancellationToken);

    /// <summary>The Planner's Carrier bookings awaiting an answer: the Requested ones, the earliest collection first, each flagged when overdue.</summary>
    Task<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>> ListCarrierBookingsAwaitingAnswerAsync(CancellationToken cancellationToken);
}

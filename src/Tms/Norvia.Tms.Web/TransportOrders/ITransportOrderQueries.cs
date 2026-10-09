using Norvia.Tms.Application.Billing.ListNotYetBillable;
using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.TrackingEvents.Timeline;
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

    /// <summary>The Delivered Transport-only orders that aren't Billable yet (P9), oldest delivery first, each with its blocking reasons.</summary>
    Task<IReadOnlyList<TransportOrderNotYetBillableSummary>> ListNotYetBillableAsync(CancellationToken cancellationToken);

    /// <summary>The Carriers a Planner may assign to: only the active ones, by name.</summary>
    Task<IReadOnlyList<CarrierView>> ListActiveCarriersAsync(CancellationToken cancellationToken);

    /// <summary>The Own fleet's active Vehicles (P10), by registration.</summary>
    Task<IReadOnlyList<VehicleView>> ListVehiclesAsync(CancellationToken cancellationToken);

    /// <summary>The Own fleet's active Drivers (P10), by name, each with the licences they hold.</summary>
    Task<IReadOnlyList<DriverView>> ListDriversAsync(CancellationToken cancellationToken);

    /// <summary>The Shipment with this number, or null when there is none.</summary>
    Task<ShipmentView?> FindShipmentAsync(string shipmentNumber, CancellationToken cancellationToken);

    /// <summary>The Planner's Shipments to release: the Planned Shipments, with who carries each, in the order they were planned.</summary>
    Task<IReadOnlyList<ShipmentToReleaseSummary>> ListShipmentsToReleaseAsync(CancellationToken cancellationToken);

    /// <summary>The Planner's Carrier bookings awaiting an answer: the Requested ones, the earliest collection first, each flagged when overdue.</summary>
    Task<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>> ListCarrierBookingsAwaitingAnswerAsync(CancellationToken cancellationToken);

    /// <summary>The Planner's Shipments in progress: the Booked, Collected and In transit ones, Stale first and then by ETA.</summary>
    Task<IReadOnlyList<ShipmentInProgressSummary>> ListShipmentsInProgressAsync(CancellationToken cancellationToken);

    /// <summary>The Planner's Delivered Shipments without a current Proof of delivery: the flagged ones first, then the oldest delivered.</summary>
    Task<IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>> ListDeliveredShipmentsWithoutProofOfDeliveryAsync(CancellationToken cancellationToken);

    /// <summary>The Shipment's Tracking events in event-time order and its Proofs of delivery, or null when there is no such Shipment.</summary>
    Task<ShipmentTimeline?> FindTrackingTimelineAsync(string shipmentNumber, CancellationToken cancellationToken);

    /// <summary>The Planner's Open Exceptions (P7), oldest first.</summary>
    Task<IReadOnlyList<OpenExceptionSummary>> ListOpenExceptionsAsync(CancellationToken cancellationToken);

    /// <summary>The message Notify Shipper starts with for the type of this Exception, or null when there is no such Exception.</summary>
    Task<string?> FindShipperNotificationTemplateAsync(string exceptionNumber, CancellationToken cancellationToken);
}

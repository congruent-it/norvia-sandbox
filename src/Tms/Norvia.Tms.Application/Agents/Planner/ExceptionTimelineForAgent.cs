using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>What the TMS knows of one Exception and the Shipment it happened to, for the Planner agent to draft a note from.</summary>
/// <param name="ShipmentStatus">Where the Shipment stands now.</param>
/// <param name="CarriedBy">Who carries the Shipment (the Carrier or Own fleet).</param>
/// <param name="Exception">The Exception: causes, notes, Shipper notifications, Carrier booking cancellations, outcome.</param>
/// <param name="TrackingEvents">The Shipment's Tracking events in event-time order, with their sources.</param>
/// <param name="CarrierBookings">The Shipment's Carrier bookings in the order requested.</param>
/// <param name="AgreedDeliveryWindows">The delivery windows the Planner agreed, in the order set.</param>
public sealed record ExceptionTimelineForAgent(
    string ShipmentNumber,
    ShipmentStatus ShipmentStatus,
    CarriedByView CarriedBy,
    ExceptionTimelineEntry Exception,
    IReadOnlyList<TrackingTimelineEntry> TrackingEvents,
    IReadOnlyList<CarrierBookingView> CarrierBookings,
    IReadOnlyList<AgreedDeliveryWindowView> AgreedDeliveryWindows);

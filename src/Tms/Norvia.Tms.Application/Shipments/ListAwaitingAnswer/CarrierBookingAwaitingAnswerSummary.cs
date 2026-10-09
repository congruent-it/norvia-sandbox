using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Application.Shipments.ListAwaitingAnswer;

/// <summary>
/// A Requested Carrier booking in the Planner's list of bookings awaiting an answer (P4): the Shipment it covers, the
/// Carrier it asks, when it was requested, when the Carrier acknowledged receipt (or not yet), whether it is overdue, and
/// the Carrier cost and its source that the Carrier is asked to accept (P9).
/// </summary>
public sealed record CarrierBookingAwaitingAnswerSummary(
    string ShipmentNumber,
    string CarrierBookingNumber,
    CarrierView Carrier,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? EarliestCollectionTime,
    bool Overdue,
    CarrierCostView? CarrierCost = null);

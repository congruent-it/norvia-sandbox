namespace Norvia.Tms.Web.Planner;

/// <summary>What needs the Planner to act, as the Planner's home page counts it; each count is the length of the list it links to.</summary>
/// <param name="WaitingForPlanning">Transport Orders waiting for planning.</param>
/// <param name="ShipmentsToRelease">Planned Shipments to release.</param>
/// <param name="OverdueBookings">Carrier bookings awaiting an answer that are overdue.</param>
/// <param name="OpenExceptions">Open Exceptions.</param>
/// <param name="DeliveredWithoutProofOfDelivery">Delivered Shipments without a current Proof of delivery.</param>
/// <param name="ShipmentsToPutOnTrips">Released Own fleet Shipments not on a Trip.</param>
public sealed record PlannerHomeCounts(
    int WaitingForPlanning,
    int ShipmentsToRelease,
    int OverdueBookings,
    int OpenExceptions,
    int DeliveredWithoutProofOfDelivery,
    int ShipmentsToPutOnTrips);

namespace Norvia.Tms.Web.Planner;

/// <summary>One thing the Planner's home page shows: a count, the list it links to, the action to take there, and what to say when there is nothing to do.</summary>
internal sealed record PlannerHomeItem(string Title, string Path, int Count, string Unit, string Action, string NothingToDo)
{
    /// <summary>The items in the order the Planner works: plan, release, follow up, then the Own fleet.</summary>
    public static IReadOnlyList<PlannerHomeItem> From(PlannerHomeCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return
        [
            new("Waiting for planning", "/transport-orders", counts.WaitingForPlanning, "Transport Orders to plan", "Plan them", "No Transport Orders are waiting for planning."),
            new("Shipments to release", "/shipments/to-release", counts.ShipmentsToRelease, "Shipments to release", "Release them", "No Shipments are waiting to be released."),
            new("Bookings awaiting an answer", "/shipments/awaiting-answer", counts.OverdueBookings, "Carrier bookings overdue for an answer", "Chase the Carriers", "No Carrier booking is overdue for an answer."),
            new("Open Exceptions", "/exceptions", counts.OpenExceptions, "Exceptions open", "Handle them", "No Exceptions are open."),
            new("Delivered without Proof of delivery", "/shipments/delivered-without-proof-of-delivery", counts.DeliveredWithoutProofOfDelivery, "Delivered Shipments without Proof of delivery", "Add the Proof of delivery", "Every delivered Shipment has its Proof of delivery."),
            new("Shipments to put on Trips", "/trips/shipments", counts.ShipmentsToPutOnTrips, "Shipments to put on Trips", "Put them on Trips", "No Shipments are waiting for a Trip."),
        ];
    }
}

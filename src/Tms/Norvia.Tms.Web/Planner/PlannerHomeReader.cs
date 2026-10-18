using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.Planner;

/// <summary>
/// Reads the counts of the Planner's home page from the lists they link to, so a count and its list never disagree and
/// the home page needs no table of its own. A Carrier booking counts when the Planner's list flags it overdue, which
/// is the same rule that flags it there.
/// </summary>
internal static class PlannerHomeReader
{
    public static async Task<IReadOnlyList<PlannerHomeCounts>> ReadAsync(ITransportOrderQueries queries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queries);

        // Each query takes a scope of its own, so they can run together.
        var waiting = queries.ListWaitingForPlanningAsync(cancellationToken);
        var toRelease = queries.ListShipmentsToReleaseAsync(cancellationToken);
        var awaitingAnswer = queries.ListCarrierBookingsAwaitingAnswerAsync(cancellationToken);
        var exceptions = queries.ListOpenExceptionsAsync(cancellationToken);
        var withoutProof = queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(cancellationToken);
        var toPutOnTrips = queries.ListShipmentsToPutOnTripsAsync(null, cancellationToken);
        await Task.WhenAll(waiting, toRelease, awaitingAnswer, exceptions, withoutProof, toPutOnTrips);

        // A list of one, because the live list that keeps the page current reads lists.
        return [new PlannerHomeCounts(
            (await waiting).Count,
            (await toRelease).Count,
            (await awaitingAnswer).Count(booking => booking.Overdue),
            (await exceptions).Count,
            (await withoutProof).Count,
            (await toPutOnTrips).Count)];
    }
}

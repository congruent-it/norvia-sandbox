namespace Norvia.Tms.Application.Reset;

/// <summary>The TMS's database as a reset sees it: transactional records go, reference data stays, and what the seed file provides is in force again.</summary>
public interface ITransactionalData
{
    /// <summary>
    /// Deletes the Transport Orders and everything hanging off them, the Shipments, Trips, Exceptions, Carrier bookings, Tracking events,
    /// Proof of delivery records, Shipper notifications and outbox Messages, and the Planner's changes to the Rosters, Absences and Off the road
    /// days, so the seed file's Rosters apply again. It restarts the number sequences.
    /// </summary>
    Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken);
}

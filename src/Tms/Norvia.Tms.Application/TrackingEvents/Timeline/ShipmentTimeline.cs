using Norvia.Tms.Application.ProofsOfDelivery.Read;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

/// <summary>What happened to a Shipment on its way: what each Carrier reported, and the Proofs of delivery that came in.</summary>
/// <param name="TrackingEvents">In event-time order.</param>
/// <param name="ProofsOfDelivery">Current and superseded, in delivery-time order; the current one says so.</param>
public sealed record ShipmentTimeline(IReadOnlyList<TrackingTimelineEntry> TrackingEvents, IReadOnlyList<ProofOfDeliveryView> ProofsOfDelivery);

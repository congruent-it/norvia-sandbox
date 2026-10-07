using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.ProofsOfDelivery.Read;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

/// <summary>
/// What happened to a Shipment on its way: what each Carrier reported, the Proofs of delivery that came in, and what went wrong
/// and what the Planner did about it.
/// </summary>
/// <param name="TrackingEvents">In event-time order.</param>
/// <param name="ProofsOfDelivery">Current and superseded, in delivery-time order; the current one says so.</param>
/// <param name="Exceptions">Open and resolved (P7), in the order they opened.</param>
/// <param name="AgreedDeliveryWindows">The delivery windows the Planner agreed (P7), in the order they were set; the last one counts.</param>
public sealed record ShipmentTimeline(
    IReadOnlyList<TrackingTimelineEntry> TrackingEvents,
    IReadOnlyList<ProofOfDeliveryView> ProofsOfDelivery,
    IReadOnlyList<ExceptionTimelineEntry> Exceptions,
    IReadOnlyList<AgreedDeliveryWindowView> AgreedDeliveryWindows);

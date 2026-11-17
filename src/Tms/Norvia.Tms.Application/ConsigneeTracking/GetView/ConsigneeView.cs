using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Application.ConsigneeTracking.GetView;

/// <summary>
/// The Consignee's view of one Transport Order, the Canonical Model's consignee-transport-order-view (ADR 0052). It is the
/// Shipper's projection (ADR 0011) with the Shipper-only items taken out, made as a read model of its own, not by filtering the
/// Shipper's: no Shipper reference, Shipper, Carrier, Shipment number, Exception, remark, Stale or price, and no personal data.
/// </summary>
/// <param name="TransportOrderNumber">Norvia's number. It is for the logs only, which never carry the code: the page doesn't show it.</param>
/// <param name="DeliveryWindow">The agreed delivery window when the Planner has set one (P7), otherwise the Transport Order's.</param>
/// <param name="Eta">The ETA of the last Tracking event that carried one, otherwise the end of the delivery window; null when there is none.</param>
/// <param name="LastUpdate">The event time of the latest progress Tracking event; null before collection.</param>
/// <param name="ProofOfDelivery">The current one, without its files; null when there is none.</param>
public sealed record ConsigneeView(
    string TransportOrderNumber,
    ShipperPlaceView? Collection,
    ShipperPlaceView? Delivery,
    ShipperWindowView? DeliveryWindow,
    TrackingStatus TrackingStatus,
    DateTimeOffset? Eta,
    DateTimeOffset? LastUpdate,
    IReadOnlyList<ShipperProgressEntry> Progress,
    ConsigneeProofOfDelivery? ProofOfDelivery);

/// <summary>What the Consignee signed for. Its files can hold other people's names and signatures, so they stay out.</summary>
public sealed record ConsigneeProofOfDelivery(string ReceivedBy, DateTimeOffset DeliveryTime, DeliveryCondition Condition);

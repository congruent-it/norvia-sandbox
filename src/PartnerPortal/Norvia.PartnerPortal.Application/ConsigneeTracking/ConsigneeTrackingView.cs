using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.ConsigneeTracking;

/// <summary>
/// What a Consignee sees of its one Transport Order (ADR 0052): the Consignee tracking format, with every time a UTC instant. It has no
/// Shipper reference, Shipper, Carrier, Shipment number, Transport Order number, Exception, remark or price, and no Proof of delivery files.
/// </summary>
/// <param name="DeliverFrom">The earliest time of the delivery window; the agreed window when there is one.</param>
/// <param name="Eta">The estimated time of arrival; null when there is none.</param>
/// <param name="LastUpdate">The time of the latest progress event; null before collection, as on the Shipper's screen.</param>
public sealed record ConsigneeTrackingView(
    ShipperTrackingStatus Status,
    ShipperPlace Pickup,
    ShipperPlace Receiver,
    DateTimeOffset? DeliverFrom,
    DateTimeOffset? DeliverUntil,
    DateTimeOffset? Eta,
    DateTimeOffset? LastUpdate,
    IReadOnlyList<ConsigneeProgressEvent> Progress,
    ConsigneeProofOfDelivery? ProofOfDelivery);

public sealed record ConsigneeProgressEvent(ShipperProgressType Type, DateTimeOffset Time, ShipperPlace Place);

/// <summary>What the Consignee signed for. Its files stay with Norvia.</summary>
public sealed record ConsigneeProofOfDelivery(string ReceivedBy, DateTimeOffset DeliveredAt, ProofOfDeliveryCondition Condition);

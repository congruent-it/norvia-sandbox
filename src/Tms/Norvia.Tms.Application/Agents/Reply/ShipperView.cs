using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;

namespace Norvia.Tms.Application.Agents.Reply;

/// <summary>
/// What a Shipper sees of one Transport Order, and nothing more (ADR 0011): the Shipper tracking read model and the Shipper notifications
/// the Planner already sent. It holds no Carrier, no Shipment number, no Exception and no remark.
/// </summary>
/// <param name="Tracking">The Shipper tracking read model, as the Shipper's own tracking call returns it.</param>
/// <param name="Notifications">The notifications that reached the Shipper (Sent), oldest first.</param>
public sealed record ShipperView(ShipperTransportOrderDetail Tracking, IReadOnlyList<ShipperNotificationSent> Notifications);

/// <summary>A notification the Planner sent the Shipper: when, and the text the Shipper received.</summary>
public sealed record ShipperNotificationSent(DateTimeOffset SentAt, string Message);

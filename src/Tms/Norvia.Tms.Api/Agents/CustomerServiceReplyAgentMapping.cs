using Norvia.Tms.Application.Agents.Reply;

namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The Shipper's view of a Transport Order as the reply agent reads it (<c>get_shipper_view</c>), field by field (ADR 0011): the five
/// Tracking statuses, progress events, whether a Proof of delivery exists and the Shipper notifications already sent. No Carrier, no
/// Shipment number, no Exception, no remark, no signer and no file: nothing the Shipper does not see. AgentViewsTests holds the list.
/// </summary>
/// <param name="TrackingStatus">Received, Scheduled, Collected, InTransit or Delivered.</param>
/// <param name="AsOf">When the view was read, so a draft can say how current it is.</param>
/// <param name="Progress">Progress events in event-time order.</param>
/// <param name="ProofOfDelivery">Whether a Proof of delivery is available; its delivery time and condition when it is.</param>
/// <param name="Notifications">What the Planner already sent the Shipper, oldest first.</param>
public sealed record ReplyAgentTrackingView(
    string TransportOrderNumber,
    string? ShipperReference,
    string TrackingStatus,
    DateTimeOffset AsOf,
    ReplyAgentPlaceView? Collection,
    ReplyAgentPlaceView? Delivery,
    ReplyAgentWindowView? DeliveryWindow,
    DateTimeOffset? Eta,
    DateTimeOffset? LastUpdate,
    IReadOnlyList<ReplyAgentProgressView> Progress,
    ReplyAgentProofOfDeliveryView ProofOfDelivery,
    IReadOnlyList<ReplyAgentNotificationView> Notifications);

public sealed record ReplyAgentPlaceView(string? City, string? Country);

public sealed record ReplyAgentWindowView(DateTimeOffset? Earliest, DateTimeOffset? Latest);

/// <param name="Type">Collected, Departed, Arrived, OutForDelivery or Delivered.</param>
public sealed record ReplyAgentProgressView(string Type, DateTimeOffset EventTime, ReplyAgentPlaceView Place);

/// <param name="Available">Whether the Shipper can download a Proof of delivery.</param>
/// <param name="DeliveryTime">When the goods were delivered, by the Proof of delivery; null when none is available.</param>
/// <param name="Condition">Clean or Reservations; null when none is available.</param>
public sealed record ReplyAgentProofOfDeliveryView(bool Available, DateTimeOffset? DeliveryTime, string? Condition);

public sealed record ReplyAgentNotificationView(DateTimeOffset SentAt, string Message);

internal static class CustomerServiceReplyAgentMapping
{
    public static ReplyAgentTrackingView Of(ShipperView view, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(view);

        var tracking = view.Tracking;
        return new ReplyAgentTrackingView(
            tracking.TransportOrderNumber,
            tracking.ShipperReference,
            tracking.TrackingStatus.ToString(),
            asOf,
            tracking.Collection is { } collection ? new ReplyAgentPlaceView(collection.City, collection.CountryCode) : null,
            tracking.Delivery is { } delivery ? new ReplyAgentPlaceView(delivery.City, delivery.CountryCode) : null,
            tracking.DeliveryWindow is { } window ? new ReplyAgentWindowView(window.Earliest, window.Latest) : null,
            tracking.Eta,
            tracking.LastUpdate,
            [.. tracking.Progress.Select(entry => new ReplyAgentProgressView(entry.Type, entry.EventTime, new ReplyAgentPlaceView(entry.Place.City, entry.Place.CountryCode)))],
            tracking.ProofOfDelivery is { } proof
                ? new ReplyAgentProofOfDeliveryView(true, proof.DeliveryTime, proof.Condition.ToString())
                : new ReplyAgentProofOfDeliveryView(false, null, null),
            [.. view.Notifications.Select(notification => new ReplyAgentNotificationView(notification.SentAt, notification.Message))]);
    }
}

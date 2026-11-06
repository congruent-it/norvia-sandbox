using Norvia.Tms.Application.Billing.ListNotYetBillable;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.Agents;

/// <summary>The Transport Order half of the agent view's mapping: field by field, as for a Rejected booking.</summary>
internal static partial class CustomerServiceAgentMapping
{
    /// <param name="shipmentStatus">The Shipment's status; null when the order has no Shipment.</param>
    /// <param name="timeline">The Shipment's timeline, which says whether it has a current Proof of delivery and an open Exception; null without a Shipment.</param>
    public static AgentTransportOrderView Of(TransportOrderView order, ShipmentStatus? shipmentStatus, ShipmentTimeline? timeline)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new AgentTransportOrderView(
            order.TransportOrderNumber ?? string.Empty,
            order.Status.ToString(),
            order.ReceivedAt,
            order.CorrelationId,
            order.Channel.ToString(),
            new AgentShipperView(order.Shipper.Id, order.Shipper.Name),
            order.TransportOrder.ShipperReference,
            order.ShipmentNumber,
            shipmentStatus?.ToString(),
            order.RatingResult is { } result
                ? new AgentRatingView(result.ToString(), order.RatingFailure?.Code, order.RatingFailure?.Message)
                : null,
            order.BillingState is { } state
                ? new AgentBillingView(state.ToString(), timeline?.Billing?.BecameBillableAt, order.ProofOfDeliveryRelease is not null, BlockingReasons(order, state, timeline))
                : null);
    }

    // The same conditions as Customer service's list of orders that are not yet billable (P9): only a Delivered Transport-only order has any.
    private static List<string> BlockingReasons(TransportOrderView order, BillingState state, ShipmentTimeline? timeline)
    {
        if (state != BillingState.NotYetBillable || timeline is null)
        {
            return [];
        }

        var reasons = new List<string>();
        if (order.RatingResult != RatingResult.Rated)
        {
            reasons.Add(nameof(BillingBlockingReason.NotRated));
        }

        if (order.ProofOfDeliveryRelease is null && !timeline.ProofsOfDelivery.Any(proof => proof.IsCurrent))
        {
            reasons.Add(nameof(BillingBlockingReason.NoProofOfDelivery));
        }

        if (timeline.Exceptions.Any(exception => exception.Status == ExceptionStatus.Open))
        {
            reasons.Add(nameof(BillingBlockingReason.OpenException));
        }

        return reasons;
    }

    public static AgentShipmentTimelineView Of(string shipmentNumber, ShipmentTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return new AgentShipmentTimelineView(
            shipmentNumber,
            [.. timeline.TrackingEvents.Select(trackingEvent => new AgentTrackingEventView(
                trackingEvent.Type.ToString(),
                trackingEvent.EventTime,
                trackingEvent.Place,
                trackingEvent.CountryCode,
                trackingEvent.Eta,
                trackingEvent.ReceivedAt,
                trackingEvent.MovedShipment,
                trackingEvent.Source))]);
    }

    public static AgentExceptionsView ExceptionsOf(string shipmentNumber, ShipmentTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return new AgentExceptionsView(
            shipmentNumber,
            [.. timeline.Exceptions.Select(exception =>
            {
                var sent = exception.Notifications.Where(notification => notification.SentAt is not null).Select(notification => notification.SentAt).Max();
                return new AgentExceptionView(
                    exception.ExceptionNumber,
                    exception.Type.ToString(),
                    exception.Status.ToString(),
                    exception.OpenedAt,
                    exception.AfterDelivered,
                    [.. exception.Causes.Select(cause => cause.Kind.ToString()).Distinct()],
                    [.. exception.Notes.Select(note => new AgentExceptionNoteView(note.Text, note.AddedAt))],
                    sent is not null,
                    sent,
                    exception.Outcome?.ToString(),
                    exception.ResolvedAt);
            })]);
    }

    /// <param name="deliveredShipmentWithoutProofIsMissing">For a Delivered Shipment without a current Proof of delivery: whether the TMS's missing threshold has passed. Null when the Shipment is not in that list.</param>
    public static AgentProofOfDeliveryStatusView ProofOfDeliveryOf(string shipmentNumber, ShipmentStatus status, ShipmentTimeline timeline, bool? deliveredShipmentWithoutProofIsMissing)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var current = timeline.ProofsOfDelivery.FirstOrDefault(proof => proof.IsCurrent);
        var state = current is not null
            ? "Current"
            : status != ShipmentStatus.Delivered
                ? "NotDelivered"
                : deliveredShipmentWithoutProofIsMissing == true ? "Missing" : "NotDueYet";
        return new AgentProofOfDeliveryStatusView(
            shipmentNumber,
            state,
            current is null ? null : current.Condition == Norvia.Tms.Domain.ProofsOfDelivery.DeliveryCondition.Reservations,
            [.. timeline.ProofsOfDelivery.Select(proof => new AgentProofOfDeliveryView(
                proof.Source, proof.DeliveryTime, proof.Condition.ToString(), proof.IsCurrent, proof.ReceivedAt, proof.Files.Count))]);
    }
}

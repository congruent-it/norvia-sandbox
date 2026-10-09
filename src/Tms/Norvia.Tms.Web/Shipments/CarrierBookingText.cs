using System.Globalization;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.Shipments;

/// <summary>Words a Carrier booking's outcome as a Planner reads it, in the glossary's terms.</summary>
public static class CarrierBookingText
{
    public const string NotYetSent = "Not yet sent";

    /// <summary>The flag on a Withdrawn or Cancelled booking whose cancellation message never reached the Carrier (P7).</summary>
    public const string CarrierNotTold = "Carrier not told";

    /// <summary>A Shipment's last Carrier booking outcome at a glance, such as "Rejected by Kessler Spedition: No capacity".</summary>
    public static string LastOutcome(LastCarrierBookingOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var carrier = PlanningText.Carrier(outcome.Carrier);
        var text = outcome.Status switch
        {
            CarrierBookingStatus.Rejected => $"Rejected by {carrier}: {Reason(outcome.ReasonCode, outcome.ReasonText)}",
            CarrierBookingStatus.NotDelivered => $"Not delivered to {carrier}",
            CarrierBookingStatus.Withdrawn => $"Withdrawn from {carrier}",
            CarrierBookingStatus.Cancelled => $"Cancelled by Norvia: {outcome.ReasonText}",
            _ => $"{outcome.Status} by {carrier}",
        };
        return outcome.CarrierNotTold ? $"{text}. {CarrierNotTold}: {outcome.CarrierNotToldMessage}" : text;
    }

    /// <summary>What a Carrier booking came to, past its status: the Carrier's rejection reason, its Carrier booking reference, or why it wasn't delivered.</summary>
    public static string Detail(CarrierBookingView booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return booking.Status switch
        {
            CarrierBookingStatus.Rejected => Reason(booking.ReasonCode, booking.ReasonText),
            CarrierBookingStatus.Confirmed => $"Carrier booking reference {booking.CarrierBookingReference}",
            CarrierBookingStatus.NotDelivered => booking.NotDeliveredMessage ?? string.Empty,
            CarrierBookingStatus.Withdrawn when booking.WithdrawnAt is { } at => $"Withdrawn {NorviaTime.Format(at)}",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// A Carrier cost and its source at a glance, such as "412.50 EUR · Carrier Rate Card version 3" or "480.00 EUR · Spot price: agreed by phone"
    /// (P9); empty for a booking without one.
    /// </summary>
    public static string CarrierCost(CarrierCostView? cost)
    {
        if (cost is null)
        {
            return string.Empty;
        }

        var source = cost.Source switch
        {
            { Kind: nameof(CarrierCostSourceKind.Spot) } spot => string.IsNullOrWhiteSpace(spot.Remark) ? "Spot price" : $"Spot price: {spot.Remark}",
            { RateCardVersion: { } version } => $"Carrier Rate Card version {version.Version}",
            _ => "Carrier Rate Card",
        };
        return $"{cost.Total.ToString("0.00", CultureInfo.InvariantCulture)} {cost.Currency} · {source}";
    }

    /// <summary>When the Carrier acknowledged receipt of the request, or that it hasn't yet.</summary>
    public static string Sent(DateTimeOffset? sentAt) => sentAt is { } at ? NorviaTime.Format(at) : NotYetSent;

    private static string Reason(CarrierBookingRejectionReason? code, string? text) =>
        !string.IsNullOrWhiteSpace(text) ? text : code switch
        {
            CarrierBookingRejectionReason.NoCapacity => "No capacity",
            CarrierBookingRejectionReason.LaneNotServed => "Lane not served",
            CarrierBookingRejectionReason.WindowNotFeasible => "Window not feasible",
            CarrierBookingRejectionReason.GoodsNotAccepted => "Goods not accepted",
            CarrierBookingRejectionReason.PriceNotAccepted => "Price not accepted",
            _ => "No reason given",
        };
}

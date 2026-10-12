using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.Shipments;

/// <summary>Words a Tracking event as a Planner reads it, in the glossary's terms.</summary>
public static class TrackingText
{
    public const string NoneYet = "None yet";

    /// <summary>A Shipment's status as the glossary names it, such as "In transit".</summary>
    public static string Status(ShipmentStatus status) => status == ShipmentStatus.InTransit ? "In transit" : status.ToString();

    /// <summary>What a Tracking event reports, such as "Out for delivery".</summary>
    public static string Type(TrackingEventType type) => type switch
    {
        TrackingEventType.OutForDelivery => "Out for delivery",
        TrackingEventType.CollectionFailed => "Collection failed",
        TrackingEventType.DeliveryFailed => "Delivery failed",
        _ => type.ToString(),
    };

    /// <summary>Where a Tracking event happened, such as "Rotterdam, 3011 AA, NL".</summary>
    public static string Place(string place, string postcode, string countryCode) =>
        string.Join(", ", new[] { place, postcode, countryCode }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>A Shipment's last Tracking event at a glance, such as "Collected, Thu 24 Sep 2026, 11:45, Rotterdam, 3011 AA, NL", or that none has come.</summary>
    public static string Last(LastTrackingEventView? last) => last is null
        ? NoneYet
        : $"{Type(last.Type)}, {NorviaTime.Format(last.EventTime)}, {Place(last.Place, last.Postcode, last.CountryCode)}";

    /// <summary>Who carries a Shipment in progress, and for Own fleet its Trip: "Own fleet · TR-0000042".</summary>
    public static string CarriedBy(ShipmentInProgressSummary shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        var carriedBy = TransportOrders.PlanningText.CarriedBy(shipment.CarriedBy);
        return shipment.TripNumber is null ? carriedBy : $"{carriedBy} · {shipment.TripNumber}";
    }
}

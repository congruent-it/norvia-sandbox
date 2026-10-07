using System.Globalization;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.ShipperTracking;

/// <summary>Words for what a Shipper sees of a Transport Order's tracking.</summary>
public static class TrackingText
{
    public static string Status(ShipperTrackingStatus status) => status switch
    {
        ShipperTrackingStatus.Received => "Received",
        ShipperTrackingStatus.Scheduled => "Scheduled",
        ShipperTrackingStatus.Collected => "Collected",
        ShipperTrackingStatus.InTransit => "In transit",
        ShipperTrackingStatus.Delivered => "Delivered",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Every Tracking status has words."),
    };

    public static string Progress(ShipperProgressType type) => type switch
    {
        ShipperProgressType.Collected => "Collected",
        ShipperProgressType.Departed => "Departed",
        ShipperProgressType.Arrived => "Arrived",
        ShipperProgressType.OutForDelivery => "Out for delivery",
        ShipperProgressType.Delivered => "Delivered",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Every progress step has words."),
    };

    public static string Condition(ProofOfDeliveryCondition condition) => condition switch
    {
        ProofOfDeliveryCondition.Clean => "Delivered clean",
        ProofOfDeliveryCondition.Reservations => "Delivered with reservations",
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Every condition has words."),
    };

    /// <summary>A time as the API returns it, or a dash for none: an empty last update is how a Shipper sees the silence before collection.</summary>
    public static string Time(string? time) => time ?? "–";

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes.ToString(CultureInfo.InvariantCulture)} B",
        < 1024 * 1024 => $"{(bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} kB",
        _ => $"{(bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture)} MB",
    };
}

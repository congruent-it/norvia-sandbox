using Norvia.Tms.Domain.ProofsOfDelivery;

namespace Norvia.Tms.Web.Shipments;

/// <summary>Words a Proof of delivery as a Planner reads it, in the glossary's terms.</summary>
public static class ProofOfDeliveryText
{
    /// <summary>The marker on a Shipment in progress whose Proof of delivery arrived before its Delivered Tracking event.</summary>
    public const string EarlyMarker = "Proof of delivery received";

    /// <summary>What a Proof of delivery says about the goods, such as "Clean" or "With reservations".</summary>
    public static string Condition(DeliveryCondition condition) => condition == DeliveryCondition.Reservations ? "With reservations" : condition.ToString();

    /// <summary>A file's size at a glance, such as "512 B", "48 KB" or "2.3 MB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.0} MB",
    };
}

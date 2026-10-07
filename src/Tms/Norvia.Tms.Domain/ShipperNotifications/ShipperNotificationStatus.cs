namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>Where a Shipper notification stands (P7). The names are stable: never rename one, only add.</summary>
public enum ShipperNotificationStatus
{
    /// <summary>Created by the Planner, not yet acknowledged by the Shipper.</summary>
    Requested = 1,

    /// <summary>The Shipper's endpoint acknowledged it. Final.</summary>
    Sent,

    /// <summary>It never reached the Shipper, with the technical cause. Final.</summary>
    NotDelivered,
}

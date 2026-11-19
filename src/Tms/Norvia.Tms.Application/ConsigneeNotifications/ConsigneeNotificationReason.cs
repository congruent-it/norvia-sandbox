namespace Norvia.Tms.Application.ConsigneeNotifications;

/// <summary>Why the Consignee is told where to track the goods (#983). The names are stable: they are in the event and in the email's wording.</summary>
public static class ConsigneeNotificationReason
{
    /// <summary>The Shipment was Collected (P5).</summary>
    public const string Collected = "Collected";

    /// <summary>The Planner agreed a delivery window for the Shipment (P7).</summary>
    public const string DeliveryWindowAgreed = "DeliveryWindowAgreed";
}

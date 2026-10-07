namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>The notification ID of a Shipper notification: the key the Shipper notification Logic App and the Partner portal know it by (P7).</summary>
public readonly record struct ShipperNotificationId(Guid Value)
{
    public static ShipperNotificationId New() => new(Guid.CreateVersion7());

    public static bool TryParse(string? text, out ShipperNotificationId id)
    {
        var parsed = Guid.TryParseExact(text, "D", out var value);
        id = new(value);
        return parsed;
    }

    public override string ToString() => Value.ToString("D");
}

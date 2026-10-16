namespace Norvia.Simulator.Domain.Drivers;

/// <summary>What a Driver's actions are called: the log's action, and the span the Simulator sends the call in, which are the Driver app's names for the same actions.</summary>
public static class DriverActions
{
    public const string StartTrip = "Start Trip";

    public const string Collected = "Collected";

    public const string CollectionFailed = "Collection failed";

    public const string Deliver = "Deliver";

    public const string DeliveryFailed = "Delivery failed";

    public const string ReportDamage = "Report damage";

    public const string EndTrip = "End Trip";
}

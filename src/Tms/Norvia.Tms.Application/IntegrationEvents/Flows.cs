namespace Norvia.Tms.Application.IntegrationEvents;

/// <summary>Stable flow names of the tracking contract (coding guidelines §7).</summary>
public static class Flows
{
    public const string TransportOrderIntake = "transport-order-intake";

    /// <summary>Rating an accepted Transport Order (P2), which continues the Message trail of its intake.</summary>
    public const string TransportOrderRating = "transport-order-rating";
}

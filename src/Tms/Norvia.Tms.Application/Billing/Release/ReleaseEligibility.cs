namespace Norvia.Tms.Application.Billing.Release;

/// <summary>
/// Whether Customer service may release a Transport Order without a Proof of delivery, and what stops it (P9): the TMS decides, never an Agent.
/// </summary>
/// <param name="Eligible">True when the release is allowed and would leave nothing else in the way of the order becoming Billable.</param>
/// <param name="ShipmentNumber">The order's Shipment; null while it isn't Planned.</param>
/// <param name="DeliveredAt">When the Shipment was Delivered; null while it isn't.</param>
/// <param name="BlockingReasons">Empty when eligible. The names of <see cref="Norvia.Tms.Domain.TransportOrders.BillingRefusalCode"/> when the release itself is refused,
/// <c>ProofOfDeliveryReceived</c> when there is nothing to release, and <c>NotRated</c> or <c>OpenException</c> when releasing would not make the order Billable.</param>
public sealed record ReleaseEligibility(
    string TransportOrderNumber,
    bool Eligible,
    string? ShipmentNumber,
    DateTimeOffset? DeliveredAt,
    IReadOnlyList<string> BlockingReasons)
{
    /// <summary>A current Proof of delivery is there already: nothing to release.</summary>
    public const string ProofOfDeliveryReceived = nameof(ProofOfDeliveryReceived);

    public const string NotRated = "NotRated";

    public const string OpenException = "OpenException";
}

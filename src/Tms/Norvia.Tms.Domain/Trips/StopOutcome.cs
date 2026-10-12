namespace Norvia.Tms.Domain.Trips;

/// <summary>What the Driver reported for a Stop of a Trip (P12): a Collection Stop is Open, Collected or Collection failed, a Delivery Stop Open, Delivered or Delivery failed.</summary>
public enum StopOutcomeKind
{
    /// <summary>The Driver hasn't reported the Stop's outcome.</summary>
    Open,
    Collected,
    CollectionFailed,
    Delivered,
    DeliveryFailed,
}

/// <summary>A Stop's outcome with when it happened and the Driver's remark. An outcome is final for its Trip.</summary>
/// <param name="At">When it happened, as the Driver reported it; null while the Stop is Open.</param>
public sealed record StopOutcome(StopOutcomeKind Kind, DateTimeOffset? At = null, string? Remark = null)
{
    public static readonly StopOutcome Open = new(StopOutcomeKind.Open);

    public bool IsOpen => Kind == StopOutcomeKind.Open;
}

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The caller's key for one request to change something, so the same request sent again, as a retry, gets the same
/// answer instead of making the change twice.
/// </summary>
public sealed record IdempotencyKey
{
    public const int MaxLength = 100;

    public IdempotencyKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength, nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

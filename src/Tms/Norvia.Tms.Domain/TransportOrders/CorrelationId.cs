namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The Message trail's correlation ID, created at the API Management edge. Distinct from the Transport Order number.
/// </summary>
public sealed record CorrelationId
{
    public CorrelationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

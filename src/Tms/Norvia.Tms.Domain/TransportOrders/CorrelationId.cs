namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The Message trail's correlation ID, created at the API Management edge, or by the TMS for an action a Norvia employee
/// starts in the TMS UI. Distinct from the Transport Order number.
/// </summary>
public sealed record CorrelationId
{
    public CorrelationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <summary>A new Message trail, for an action that starts in the TMS rather than at the API Management edge.</summary>
    public static CorrelationId New() => new(Guid.CreateVersion7().ToString());

    public override string ToString() => Value;
}

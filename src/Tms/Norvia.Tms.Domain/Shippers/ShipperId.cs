namespace Norvia.Tms.Domain.Shippers;

public sealed record ShipperId
{
    public ShipperId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
